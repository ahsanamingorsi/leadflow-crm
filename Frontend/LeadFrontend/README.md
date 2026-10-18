# Leadflow CRM frontend

Angular frontend for the Leadflow ASP.NET Core API.

## Run locally

1. Start the API project and confirm it listens on `https://localhost:7178`.
2. From this folder, run `npm install` once, then `npm start` (or `ng serve`).
3. Open `http://localhost:4200`.

The Angular development server proxies `/api` requests to the API, so the browser uses one origin for the secure authentication and antiforgery cookies. The proxy is configured in `proxy.conf.json`.

## Connected API areas

- Email/password sign-in, workspace registration, current-user lookup, and sign-out.
- Contacts, companies, opportunities, and pipeline stages.
- Opportunity stage changes and activity notes.
- Workspace team member list when the signed-in account has permission.

Calendar appointments, lead-response timers, recovery/escalation actions, reporting, integrations, and Google OAuth are not included in the current API. The interface marks these areas as unavailable instead of displaying sample metrics. Google OAuth needs API routes and server-side provider configuration before it can be wired up.
