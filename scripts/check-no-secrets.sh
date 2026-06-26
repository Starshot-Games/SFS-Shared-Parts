#!/bin/sh
# Fails if any tracked content contains anti-hack markers. Run by the pre-commit hook.
if git grep -nI -e 'PROTECTION' -e 'HashControlled' -e 'hash_capture' -- ':!scripts/check-no-secrets.sh' ':!README.md' >&2; then
  echo "ERROR: anti-hack marker found above. This is a PUBLIC repo; commit aborted." >&2
  exit 1
fi
exit 0
