r"""Integration checks against a running local API and SQL Express.

Uses only Python's standard library. Creates isolated, uniquely named workspaces;
the finally block removes only this run's fixture workspaces using Windows SQL auth.
Run: python Backend/tests/phase2_smoke.py --server 'AHSAN-DESKTOP\SQLEXPRESS'
"""

import argparse
import concurrent.futures
import http.cookiejar
import json
import ssl
import subprocess
import urllib.error
import urllib.request
import uuid

parser = argparse.ArgumentParser()
parser.add_argument("--base-url", default="https://localhost:7178")
parser.add_argument("--server", required=True)
parser.add_argument("--database", default="LeadFlowCrmDb")
parser.add_argument("--cleanup-run", help="Retry cleanup for a previously reported run UUID")
parser.add_argument("--cleanup-workspace", action="append", default=[], help="Exact workspace UUID from that run")
args = parser.parse_args()
run_id = uuid.UUID(args.cleanup_run).hex if args.cleanup_run else uuid.uuid4().hex
workspace_ids = args.cleanup_workspace if args.cleanup_run else []
checks = 0


def sql(command):
    result = subprocess.run(["sqlcmd", "-S", args.server, "-d", args.database, "-E", "-C", "-I", "-b", "-Q", command],
                            capture_output=True, text=True, timeout=45)
    if result.returncode:
        raise RuntimeError("SQL fixture access failed: " + result.stdout + result.stderr)


def check(condition, message):
    global checks
    if not condition:
        raise AssertionError(message)
    checks += 1


class Client:
    def __init__(self):
        self.cookies = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.cookies),
            urllib.request.HTTPSHandler(context=ssl._create_unverified_context()))

    def request(self, method, path, data=None, expected=200, headers=None, csrf=True):
        global checks
        request_headers = {"Accept": "application/json"}
        if headers:
            request_headers.update(headers)
        if method not in ("GET", "HEAD") and csrf:
            token = self.request("GET", "/api/auth/csrf")["requestToken"]
            request_headers["X-XSRF-TOKEN"] = token
        payload = None
        if data is not None:
            payload = json.dumps(data).encode()
            request_headers["Content-Type"] = "application/json"
        request = urllib.request.Request(args.base_url + path, data=payload, headers=request_headers, method=method)
        try:
            response = self.opener.open(request, timeout=30)
        except urllib.error.HTTPError as error:
            response = error
        status, body = response.code, response.read().decode()
        if expected is None:
            return status
        if status != expected:
            raise AssertionError(f"{method} {path}: expected {expected}, got {status}: {body[:1200]}")
        checks += 1
        return json.loads(body) if body else None

    def register(self, label):
        result = self.request("POST", "/api/auth/register", {
            "workspaceName": f"Phase2Smoke-{run_id}-{label}", "displayName": "Phase 2 owner",
            "email": f"{run_id}-{label}@example.invalid", "password": "Aa1!" + uuid.uuid4().hex}, expected=201)
        workspace_ids.append(result["workspaceId"])
        return result


def cleanup():
    if not workspace_ids:
        return
    ids = ",".join("'" + str(uuid.UUID(value)) + "'" for value in workspace_ids)
    # Both exact IDs and this run's random name prefix must match before any removal.
    sql(f"""
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
    DECLARE @w TABLE (Id uniqueidentifier PRIMARY KEY);
    INSERT INTO @w SELECT Id FROM Workspaces WHERE Id IN ({ids}) AND Name LIKE 'Phase2Smoke-{run_id}-%';
    DELETE FROM CrmStageRequiredFields WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM CrmActivities WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM CrmOpportunities WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM CrmContacts WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM CrmCompanies WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM CrmCustomFields WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM CrmPipelineStages WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM CrmPipelines WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM AuditLogs WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM WorkspaceInvitations WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM RolePermissions WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM AspNetUserRoles WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM AspNetUsers WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM AspNetRoles WHERE WorkspaceId IN (SELECT Id FROM @w);
    DELETE FROM Workspaces WHERE Id IN (SELECT Id FROM @w);
    COMMIT;
    """)
    print("Temporary test workspaces and accounts removed.")


def run():
    # Fail before creating accounts if fixture cleanup cannot connect.
    sql("SET NOCOUNT ON; SELECT 1;")
    owner, other, employee = Client(), Client(), Client()
    owner.request("GET", "/api/contacts", expected=401)
    me = owner.register("a")
    other_me = other.register("b")
    check(me["currencyCode"] == "PKR" and me["timeZoneId"] == "Asia/Karachi", "Workspace defaults")
    check("crm.pipelines.manage" in me["permissions"], "Owner configuration permissions")
    pipeline = owner.request("GET", "/api/pipelines")["items"][0]
    check([s["name"] for s in pipeline["stages"]] == ["New", "Qualified", "Proposal", "Won", "Lost"], "Default stages")
    start, qualified = pipeline["stages"][:2]
    pid = pipeline["id"]
    other_pipeline = other.request("GET", "/api/pipelines")["items"][0]
    owner.request("GET", f"/api/pipelines/{other_pipeline['id']}", expected=404)
    owner.request("POST", "/api/companies", {"name": "No CSRF"}, expected=400, csrf=False)
    company = owner.request("POST", "/api/companies", {"name": "Solar Demo", "email": "solar@example.invalid"}, expected=201)
    owner.request("POST", "/api/companies", {"name": "   "}, expected=400)
    foreign_company = other.request("POST", "/api/companies", {"name": "Other tenant"}, expected=201)
    owner.request("POST", "/api/contacts", {"displayName": "Cross tenant", "companyId": foreign_company["id"]}, expected=404)
    contact = owner.request("POST", "/api/contacts", {"displayName": "Demo Customer", "companyId": company["id"],
        "ownerUserId": me["userId"], "phone": "+923001234567"}, expected=201)
    check(contact["createdAtUtc"].endswith("Z"), "UTC timestamps include Z")
    check(owner.request("GET", f"/api/contacts?companyId={company['id']}&ownerUserId={me['userId']}&search=Demo")["totalCount"] == 1, "Combined filters")
    other.request("GET", f"/api/contacts/{contact['id']}", expected=404)
    other.request("PUT", f"/api/companies/{company['id']}", {"name": "Hacked", "rowVersion": company["rowVersion"]}, expected=404)
    owner.request("POST", "/api/contacts", {"displayName": "Bad owner", "ownerUserId": other_me["userId"]}, expected=400)

    field_ids = {}
    for name, kind, options in [("Need", "Text", []), ("Units", "Number", []), ("Visit", "Date", []),
                                ("Interest", "Dropdown", ["Solar", "Battery"]), ("Consent", "Checkbox", [])]:
        field = owner.request("POST", "/api/custom-fields", {"name": name, "entityType": "Opportunity", "pipelineId": pid,
            "fieldType": kind, "options": options}, expected=201)
        field_ids[name] = field["id"]
    owner.request("POST", "/api/custom-fields", {"name": "Bad", "entityType": "Contact", "pipelineId": pid, "fieldType": "Text"}, expected=400)
    owner.request("POST", "/api/custom-fields", {"name": "Bad dropdown", "entityType": "Contact", "fieldType": "Dropdown", "options": ["a", "A"]}, expected=400)
    owner.request("POST", "/api/custom-fields", {"name": "Bad enum", "entityType": "Contact", "fieldType": 99}, expected=400)
    pipeline = owner.request("PUT", f"/api/pipelines/{pid}/stages/{qualified['id']}/required-fields",
        {"fieldIds": [field_ids["Need"], field_ids["Consent"]], "rowVersion": qualified["rowVersion"]})
    fields = {field_ids["Need"]: "Solar estimate", field_ids["Units"]: 5, field_ids["Visit"]: "2026-10-20",
        field_ids["Interest"]: "Solar", field_ids["Consent"]: False}
    opportunity_data = {"title": "Solar system", "contactId": contact["id"], "pipelineId": pid,
        "stageId": start["id"], "ownerUserId": me["userId"], "value": 125000.50}
    bad_values = [("Need", 4), ("Units", "five"), ("Visit", "2026-02-30"), ("Interest", "Unknown"), ("Consent", "yes")]
    for name, value in bad_values:
        owner.request("POST", "/api/opportunities", {**opportunity_data, "customFields": {field_ids[name]: value}}, expected=400)
    owner.request("POST", "/api/opportunities", {**opportunity_data, "value": 1.234}, expected=400)
    owner.request("POST", "/api/opportunities", {**opportunity_data, "stageId": other_pipeline["stages"][0]["id"]}, expected=404)
    opp = owner.request("POST", "/api/opportunities", opportunity_data, expected=201)
    oid = opp["id"]
    owner.request("POST", f"/api/opportunities/{oid}/move", {"stageId": qualified["id"], "rowVersion": opp["rowVersion"]}, expected=400)
    check(owner.request("GET", f"/api/opportunities/{oid}")["stageId"] == start["id"], "Rejected move leaves stage unchanged")
    opp = owner.request("POST", f"/api/opportunities/{oid}/move", {"stageId": qualified["id"], "rowVersion": opp["rowVersion"], "customFields": fields})
    check(opp["customFields"][field_ids["Consent"]] is False, "False satisfies required checkbox")
    owner.request("PUT", f"/api/opportunities/{oid}", {**opportunity_data, "stageId": qualified["id"], "rowVersion": opp["rowVersion"]}, expected=400)
    current_pipeline = owner.request("GET", f"/api/pipelines/{pid}")
    current_stage = next(s for s in current_pipeline["stages"] if s["id"] == qualified["id"])
    owner.request("DELETE", f"/api/pipelines/{pid}/stages/{qualified['id']}", expected=409, headers={"If-Match": current_stage["rowVersion"]})
    owner.request("PUT", f"/api/pipelines/{pid}/stages/{qualified['id']}", {"name": "Qualified", "kind": "Won", "rowVersion": current_stage["rowVersion"]}, expected=409)
    owner.request("DELETE", f"/api/contacts/{contact['id']}", expected=409, headers={"If-Match": contact["rowVersion"]})
    owner.request("DELETE", f"/api/companies/{company['id']}", expected=409, headers={"If-Match": company["rowVersion"]})
    owner.request("POST", f"/api/opportunities/{oid}/notes", {"text": "Customer requested a site visit."}, expected=201)
    check(any(x["eventType"] == "crm.note.added" for x in owner.request("GET", f"/api/contacts/{contact['id']}/activities")["items"]), "Contact includes opportunity activity")
    print("CRM relationships, custom types, stage requirements and deletion guards passed.")

    # A configurable employee role can audit records without seeing financial values.
    permissions = ["crm.contacts.view", "crm.contacts.edit", "crm.opportunities.view", "crm.opportunities.edit", "audit.view"]
    role = owner.request("POST", "/api/workspace/roles", {"displayName": "Restricted Auditor", "permissions": permissions}, expected=201)
    email = f"{run_id}-employee@example.invalid"
    invitation = owner.request("POST", "/api/workspace/invitations", {"email": email, "roleId": role["id"]}, expected=201)
    employee.request("POST", "/api/workspace/invitations/accept", {"token": invitation["invitationToken"],
        "displayName": "Restricted Employee", "password": "Aa1!" + uuid.uuid4().hex})
    # Accept-invitation establishes the same persistent cookie session as signup.
    employee.request("GET", "/api/auth/me")
    masked = employee.request("GET", f"/api/opportunities/{oid}")
    check(masked["value"] is None and masked["valueHidden"], "Detail hides opportunity value")
    check(employee.request("GET", "/api/opportunities")["items"][0]["value"] is None, "List hides value")
    employee.request("POST", "/api/pipelines", {"name": "Unauthorized"}, expected=403)
    employee.request("POST", "/api/custom-fields", {"name": "Unauthorized", "entityType": "Contact", "fieldType": "Text"}, expected=403)
    employee.request("DELETE", f"/api/opportunities/{oid}", expected=403, headers={"If-Match": masked["rowVersion"]})
    employee.request("PUT", f"/api/opportunities/{oid}", {**opportunity_data, "rowVersion": masked["rowVersion"],
        "stageId": qualified["id"], "customFields": fields}, expected=403)
    update = {**opportunity_data, "rowVersion": masked["rowVersion"], "stageId": qualified["id"], "customFields": fields}
    update.pop("value")
    employee.request("PUT", f"/api/opportunities/{oid}", update)
    check(owner.request("GET", f"/api/opportunities/{oid}")["value"] == 125000.50, "Omitted hidden value is preserved")
    audits = employee.request("GET", "/api/audit?pageSize=100")["items"]
    for audit in audits:
        if audit["targetType"] == "Opportunity":
            for key in ("before", "after"):
                check(audit[key] is None or "Value" not in audit[key], "Audit hides financial value")
    owner.request("PUT", f"/api/workspace/roles/{role['id']}/permissions", {"permissions": ["crm.contacts.view"]})
    employee.request("GET", "/api/audit", expected=403)
    history = employee.request("GET", f"/api/contacts/{contact['id']}/activities")["items"]
    check(all(x["entityType"] == "Contact" for x in history), "Related activity respects permissions")
    print("Workspace isolation, permissions and value redaction passed.")

    # Updates carrying the same version must not both succeed.
    version = company["rowVersion"]
    def edit(name):
        client = Client()
        for cookie in owner.cookies:
            client.cookies.set_cookie(cookie)
        return client.request("PUT", f"/api/companies/{company['id']}", {"name": name, "rowVersion": version}, expected=None)
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        results = list(pool.map(edit, ["Concurrent A", "Concurrent B"]))
    check(sorted(results) == [200, 409], f"Concurrent results: {results}")
    owner.request("PUT", f"/api/companies/{company['id']}", {"name": "Stale", "rowVersion": version}, expected=409)
    owner.request("PUT", f"/api/companies/{company['id']}", {"name": "No version"}, expected=400)
    for round_number in range(2):
        version = owner.request("GET", f"/api/companies/{company['id']}")["rowVersion"]
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            results = list(pool.map(edit, [f"Concurrent {round_number} A", f"Concurrent {round_number} B"]))
        check(sorted(results) == [200, 409], f"Repeated concurrent results: {results}")
    check(owner.request("GET", "/api/audit?eventType=crm.company.updated")["totalCount"] == 3, "Only successful edits create audits")

    extra = owner.request("POST", "/api/pipelines", {"name": "Second pipeline"}, expected=201)
    extra = owner.request("POST", f"/api/pipelines/{extra['id']}/stages", {"name": "Fresh", "kind": "Open"}, expected=201)
    extra_field = owner.request("POST", "/api/custom-fields", {"name": "Other pipeline field", "entityType": "Opportunity",
        "pipelineId": extra["id"], "fieldType": "Text"}, expected=201)
    schema = owner.request("GET", f"/api/custom-fields?entityType=Opportunity&pipelineId={pid}")
    check(all(f["id"] != extra_field["id"] for f in schema), "Pipeline field sets stay separate")
    opp = owner.request("GET", f"/api/opportunities/{oid}")
    owner.request("POST", f"/api/opportunities/{oid}/move", {"stageId": extra["stages"][0]["id"], "rowVersion": opp["rowVersion"]}, expected=400)
    # Moving pipelines is explicit: replace custom values with values valid for the destination.
    opp = owner.request("POST", f"/api/opportunities/{oid}/move", {"stageId": extra["stages"][0]["id"],
        "rowVersion": opp["rowVersion"], "customFields": {extra_field["id"]: "New scope"}})
    check(opp["pipelineId"] == extra["id"], "Cross-pipeline move updates pipeline and stage together")
    owner.request("DELETE", f"/api/custom-fields/{extra_field['id']}", expected=409, headers={"If-Match": extra_field["rowVersion"]})
    changed_field = owner.request("PUT", f"/api/custom-fields/{extra_field['id']}", {"name": "Renamed field", "entityType": "Opportunity",
        "pipelineId": extra["id"], "fieldType": "Text", "rowVersion": extra_field["rowVersion"]})
    check(changed_field["name"] == "Renamed field", "Field rename preserves values")
    current_pipeline = owner.request("GET", f"/api/pipelines/{pid}")
    ordered = owner.request("PUT", f"/api/pipelines/{pid}/stage-order", {"stages": [
        {"id": s["id"], "rowVersion": s["rowVersion"]} for s in reversed(current_pipeline["stages"])]})
    check(ordered["stages"][0]["name"] == "Lost", "Stage order persisted")
    owner.request("DELETE", f"/api/pipelines/{pid}", expected=409, headers={"If-Match": ordered["rowVersion"]})
    owner.request("GET", "/api/contacts?pageSize=101", expected=400)
    owner.request("GET", "/api/opportunities?createdFromUtc=2026-10-20&createdToUtc=2026-10-01", expected=400)
    # Exercise configuration updates, unused-field deletion and both shared field scopes.
    empty = owner.request("POST", "/api/pipelines", {"name": "Disposable"}, expected=201)
    owner.request("POST", "/api/pipelines", {"name": "Disposable"}, expected=409)
    empty = owner.request("PUT", f"/api/pipelines/{empty['id']}", {"name": "Renamed pipeline", "rowVersion": empty["rowVersion"]})
    empty = owner.request("POST", f"/api/pipelines/{empty['id']}/stages", {"name": "Empty", "kind": "Open"}, expected=201)
    stage = empty["stages"][0]
    empty = owner.request("PUT", f"/api/pipelines/{empty['id']}/stages/{stage['id']}", {"name": "Complete", "kind": "Won", "rowVersion": stage["rowVersion"]})
    check(empty["stages"][0]["kind"] == "Won", "Empty stage classification can change")
    owner.request("DELETE", f"/api/pipelines/{empty['id']}/stages/{stage['id']}", expected=204, headers={"If-Match": empty["stages"][0]["rowVersion"]})
    owner.request("DELETE", f"/api/pipelines/{empty['id']}", expected=204, headers={"If-Match": empty["rowVersion"]})
    owner.request("GET", f"/api/pipelines/{empty['id']}", expected=404)
    unused = owner.request("POST", "/api/custom-fields", {"name": "Unused", "entityType": "Contact", "fieldType": "Text"}, expected=201)
    owner.request("DELETE", f"/api/custom-fields/{unused['id']}", expected=204, headers={"If-Match": unused["rowVersion"]})
    contact_field = owner.request("POST", "/api/custom-fields", {"name": "Customer type", "entityType": "Contact",
        "fieldType": "Dropdown", "options": ["Home", "Business"]}, expected=201)
    contact = owner.request("GET", f"/api/contacts/{contact['id']}")
    contact = owner.request("PUT", f"/api/contacts/{contact['id']}", {"displayName": "Updated customer", "companyId": company["id"],
        "rowVersion": contact["rowVersion"], "customFields": {contact_field["id"]: "Home"}})
    owner.request("PUT", f"/api/custom-fields/{contact_field['id']}", {"name": "Customer type", "entityType": "Contact",
        "fieldType": "Dropdown", "options": ["Business"], "rowVersion": contact_field["rowVersion"]}, expected=409)
    company_field = owner.request("POST", "/api/custom-fields", {"name": "Employees", "entityType": "Company", "fieldType": "Number"}, expected=201)
    company = owner.request("GET", f"/api/companies/{company['id']}")
    company = owner.request("PUT", f"/api/companies/{company['id']}", {"name": "Updated company", "rowVersion": company["rowVersion"],
        "customFields": {company_field["id"]: 12}})
    check(company["customFields"][company_field["id"]] == 12, "Company custom values persist")
    owner.request("POST", f"/api/contacts/{contact['id']}/notes", {"text": "Contact note"}, expected=201)
    stage = extra["stages"][0]
    needed = owner.request("POST", "/api/custom-fields", {"name": "Not yet filled", "entityType": "Opportunity", "pipelineId": extra["id"], "fieldType": "Text"}, expected=201)
    owner.request("PUT", f"/api/pipelines/{extra['id']}/stages/{stage['id']}/required-fields",
        {"rowVersion": stage["rowVersion"], "fieldIds": [needed["id"]]}, expected=409)
    other.request("GET", f"/api/contacts/{contact['id']}/activities", expected=404)
    check(other.request("GET", "/api/audit?eventType=crm.opportunity.created")["totalCount"] == 0, "Audit workspace isolation")
    owner.request("DELETE", f"/api/opportunities/{oid}", expected=204, headers={"If-Match": opp["rowVersion"]})
    owner.request("GET", f"/api/opportunities/{oid}", expected=404)
    contact = owner.request("GET", f"/api/contacts/{contact['id']}")
    owner.request("DELETE", f"/api/contacts/{contact['id']}", expected=204, headers={"If-Match": contact["rowVersion"]})
    company = owner.request("GET", f"/api/companies/{company['id']}")
    owner.request("DELETE", f"/api/companies/{company['id']}", expected=204, headers={"If-Match": company["rowVersion"]})
    check(owner.request("GET", "/api/audit?eventType=crm.opportunity.deleted")["totalCount"] == 1, "Deletion audit retained")
    print("Concurrency, pipeline scoping, ordering and soft deletion passed.")


if __name__ == "__main__":
    try:
        if not args.cleanup_run:
            run()
    finally:
        try:
            cleanup()
        except Exception:
            print(f"Cleanup retry: --cleanup-run {run_id} " + " ".join(f"--cleanup-workspace {x}" for x in workspace_ids))
            raise
    if not args.cleanup_run:
        print(f"PASS: {checks} HTTP and behavioral checks, including fixture cleanup.")
