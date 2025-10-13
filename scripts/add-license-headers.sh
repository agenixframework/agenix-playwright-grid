#!/bin/bash
# This script applies license headers to C# files.
# It ensures exactly one license block wrapped in a #region License block.

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PYTHON_SCRIPT="$SCRIPT_DIR/fix_license_headers.py"
EDITORCONFIG=".editorconfig"

# 1. Run the python script to fix headers in all .cs files
echo "Applying license headers to .cs files..."
python3 "$PYTHON_SCRIPT"

# 2. Update .editorconfig file_header_template
# We keep the body of the license in .editorconfig so Rider/VS can still use it.
# We set the diagnostic to 'none' because dotnet format doesn't handle un-commented regions well
# and would otherwise add a duplicate header.

TEMPLATE_FILE=".templates/license-header.md"
CURRENT_YEAR=$(date +%Y)

if [ -f "$TEMPLATE_FILE" ]; then
    HEADER_CONTENT=$(sed -n '/^```/,/^```/p' "$TEMPLATE_FILE" | sed '1d;$d' | sed "s/\${CurrentDate.Year}/$CURRENT_YEAR/g")

    PROCESSED_HEADER=""
    while IFS= read -r line || [ -n "$line" ]; do
        escaped_line="${line//;/ -}"
        if [ -z "$PROCESSED_HEADER" ]; then
            PROCESSED_HEADER="$escaped_line"
        else
            PROCESSED_HEADER="$PROCESSED_HEADER\\n$escaped_line"
        fi
    done <<< "$HEADER_CONTENT"

    export PROCESSED_HEADER
    perl -i -pe 's|^file_header_template = .*|file_header_template = $ENV{PROCESSED_HEADER}|' "$EDITORCONFIG"
    perl -i -pe 's|^dotnet_diagnostic.IDE0073.severity = .*|dotnet_diagnostic.IDE0073.severity = none|' "$EDITORCONFIG"

    echo "Updated .editorconfig template and disabled IDE0073 to prevent duplication."
else
    echo "Warning: Template file $TEMPLATE_FILE not found, .editorconfig not updated."
fi

echo "License header update complete."
