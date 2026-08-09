#!/usr/bin/env bash
set -euo pipefail

rid="${1:-linux-x64}"
if [[ "$rid" != "linux-x64" ]]; then
  echo "Uso: $0 [linux-x64]" >&2
  exit 2
fi

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repository_root/src/NutManager.App/NutManager.App.csproj"
publish_directory="$repository_root/artifacts/publish/$rid"
package_directory="$repository_root/artifacts/packages"
package_path="$package_directory/NutManager-linux-x64.tar.gz"

rm -rf "$publish_directory"
mkdir -p "$publish_directory" "$package_directory"
rm -f "$package_path"

dotnet publish "$project" \
  --configuration Release \
  --runtime "$rid" \
  --self-contained true \
  --output "$publish_directory" \
  -p:PublishTrimmed=false \
  -p:PublishReadyToRun=false \
  -p:PublishSingleFile=false

if [[ ! -x "$publish_directory/NutManager.App" ]]; then
  echo "O executável publicado esperado não foi encontrado ou não tem permissão de execução." >&2
  exit 1
fi

tar -C "$publish_directory" -czf "$package_path" .
echo "Pacote criado: $package_path"
