# Shortlist

Shortlist is an applicant tracking system built as a modular monolith. The first release includes the hiring core, staff identity, and assessments.

## Run it locally

The API uses .NET 10 and PostgreSQL. The staff and candidate apps are one Angular workspace.

```bash
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"

dotnet tool restore
dotnet run --project src/Shortlist.Migrator
dotnet run --project src/Shortlist.Host
```

The API listens on http://localhost:5080. In Development it seeds Avery Quinn:

- Email: `admin@shortlist.local`
- Password: `Change-Me-Admin-1`

That password is refused outside Development and Test.

```bash
cd web
npm ci
npx ng serve
```

The dev server proxies `/api` to port 5080. Open http://localhost:4200.

A candidate invite link looks like `http://localhost:4200/take/{token}`. The code is written to the development mailbox at `GET /api/identity/v1/dev/emails` while the email provider is `Log`.

## Tests

```bash
dotnet test Shortlist.slnx
cd web && npm test -- --watch=false
bash build/check-licenses.sh
```

Integration tests use the `shortlist_test` database on localhost. Architecture tests reject references from one module into another module's internals, and they reject Entity Framework and adapter types inside Domain and Application.

## Configuration

`Storage:Provider` is `FileSystem`, `S3`, or `AzureBlob`. `Cache:Provider` is `Memory` or `Valkey`. `Email:Provider` is `Log` or `Smtp`. `Pdf:Provider` is `Html` or `Playwright`. Day-one storage is the S3-compatible adapter; the development default is the file system so the app starts without an object store.

Modules are switched with `Modules:{Name}:Enabled`.

## Licence check

NuGet and npm dependencies must use MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC, 0BSD, Unlicense, the PostgreSQL licence, or MS-PL. The check also allows BlueOak-1.0.0 (graceful-fs), MPL-2.0 (lightningcss, required by Tailwind CSS 4), CC-BY-4.0 (caniuse-lite), and OFL-1.1 (self-hosted Inter and Space Grotesk). Any other licence fails `build/check-licenses.sh`.
