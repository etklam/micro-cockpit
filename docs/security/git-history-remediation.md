# Git history remediation

Current-tree cleanup does not remove credentials from existing commits. Perform this procedure only after every exposed credential has been rotated and verified. Rewriting shared refs is intentionally not automated by this repository.

1. Record evidence that the PostgreSQL administrator, migrator, all service roles, local registration key, and internal service key have been rotated. Never reuse a former value.
2. Create a protected backup of every repository ref and record the remote branch and tag inventory.
3. Use `git-filter-repo` or an equivalent reviewed tool to remove the secret-only files `k8s/01a-db-secret.yaml` and `k8s/01b-app-secret.yaml` from every ref.
4. Create a replacement-text rules file outside the repository with every leaked value mapped to `[REDACTED]`. Apply it to retained manifests and any other affected historical blob. Protect and securely destroy the rules file after use.
5. Inspect every rewritten branch and tag. Compare the ref inventory with the backup and explicitly account for deleted or changed refs.
6. Run `gitleaks git . --redact`, `gitleaks dir . --redact`, and `python3 scripts/verify-no-plaintext-k8s-secrets.py` against the rewritten clone. Review scanner output without disclosing findings.
7. Arrange a maintenance window, protect the backup, and force-push the reviewed rewritten branches and tags only with repository-owner authorization.
8. Invalidate CI caches where possible and notify every collaborator to delete old clones and re-clone. Do not merge from an old clone.
9. Retain the incident record and rotation evidence without any credential value.

History removal does not make an exposed credential safe. Forks, clones, caches, CI logs, and external indexes may retain old objects indefinitely, so former credentials must remain revoked.
