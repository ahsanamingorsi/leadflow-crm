# LeadFrontend

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 21.1.2.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Vitest](https://vitest.dev/) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.

## Sign-in and OAuth backend contract

The Angular app now starts with a Google sign-in screen. It expects the ASP.NET Core API to provide:

- `GET /api/auth/google?returnUrl=%2F` - starts the server-side Google OAuth challenge.
- `GET /api/auth/me` - returns `200` for an authenticated session and `401` otherwise.

After successful Google sign-in, the backend should create a secure, HTTP-only session cookie and redirect to the Angular app. Keep the Google client secret on the server; do not add it to Angular environment files. In local development, configure an Angular dev-server proxy (or serve the app behind the API origin) so `/api` requests reach ASP.NET Core. OAuth cannot complete until the API endpoints, Google OAuth credentials, and allowed callback URL are configured.

Use **Preview the interface with sample data** on the sign-in screen to view the frontend without an API.
