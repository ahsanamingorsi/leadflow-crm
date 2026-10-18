const unsafeMethods = new Set(["POST", "PUT", "PATCH", "DELETE"]);

export default {
  customFetch: async (input, init = {}) => {
    const isRequest = input instanceof Request;
    const requestUrl = new URL(isRequest ? input.url : input, window.location.href);
    const method = (init.method ?? (isRequest ? input.method : "GET")).toUpperCase();
    const headers = new Headers(init.headers ?? (isRequest ? input.headers : undefined));

    if (requestUrl.origin === window.location.origin && unsafeMethods.has(method)) {
      const tokenResponse = await window.fetch("/api/auth/csrf", {
        credentials: "same-origin",
      });

      if (!tokenResponse.ok) {
        throw new Error("Could not obtain an anti-forgery token for this request.");
      }

      const { requestToken } = await tokenResponse.json();
      headers.set("X-XSRF-TOKEN", requestToken);
    }

    return window.fetch(input, {
      ...init,
      credentials: "same-origin",
      headers,
    });
  },
};
