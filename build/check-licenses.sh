#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root"

# MIT, Apache-2.0, BSD and ISC are the product allow-list.
# BlueOak-1.0.0 is the permissive licence of graceful-fs.
# MPL-2.0 is lightningcss, which Tailwind CSS 4 requires. It is file-level and does not cover Shortlist.
# CC-BY-4.0 is caniuse-lite, the browser-data set Tailwind and Angular's browserslist require.
# It is free data with an attribution duty and no revenue cap, and it does not cover Shortlist.
allow='MIT;Apache-2.0;BSD-2-Clause;BSD-3-Clause;ISC;0BSD;Unlicense;PostgreSQL;MS-PL;BlueOak-1.0.0;MPL-2.0'
npm_allow="${allow};CC-BY-4.0"

dotnet tool restore
dotnet tool run nuget-license \
  -i Shortlist.slnx \
  --allowed-license-types "$allow" \
  --licenseurl-to-license-mappings "$root/build/license-map.json" \
  -o JsonPretty > /tmp/shortlist-nuget-licenses.json

cd web
npx --yes license-checker@25.0.1 --production \
  --onlyAllow "$npm_allow;MIT OR Apache-2.0;Apache-2.0 OR MIT;(MIT OR Apache-2.0);(Apache-2.0 OR MIT)" \
  --excludePrivatePackages
