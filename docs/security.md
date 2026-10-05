# Security notes and known limitations

## Implemented controls

- Production refuses Development authentication. OIDC authorization-code flow or on-premises AD LDAPS sign-in creates a secure HttpOnly cookie. AD forms require HTTPS and never persist passwords or bypass certificate checks.
- Local staff roles are refreshed on every cookie-authenticated request; disabled accounts lose their session immediately on the next request. New AD staff start Pending unless pre-approved or configured as the initial administrator. The last enabled administrator is protected.
- Teacher and administrator policies are separate. Group membership is checked server-side before upload/publish.
- Human mutations and credential forms require antiforgery validation. Credential, pairing, and player APIs use fixed-window rate limits; native LDAP connections have bounded concurrency and timeouts.
- Device credentials are random; only SHA-256 hashes remain after one-time pairing delivery. Revocation removes the hash.
- Upload body, slide, ZIP entry, expanded-byte, compression-ratio, and path limits are enforced. ZIP signature, traversal, macros, and malformed packages are rejected before rendering.
- Converter invocation uses an argument list, absolute normalized paths, bounded output, a timeout, and no shell or listener.
- Content paths are normalised beneath the configured package root. Only allow-listed media extensions are served.
- Packages are content-addressed, immutable, and SHA-256 verified by players before atomic activation.
- Data-protection keys persist beside the data root. Protect that directory with OS ACLs and backups.
- CSP, HSTS, MIME sniffing protection, referrer/permissions policies, and correlation IDs are enabled.
- A single-instance file lock prevents two web processes sharing a SQLite/data root.

## Deployment responsibilities

Terminate TLS at a maintained reverse proxy; trust its forwarding headers only when configured to accept that proxy. Limit the management site to the school network/VPN where practical. Protect OIDC secrets with service environment ACLs. Run under a dedicated unprivileged account and restrict write access to data, keys, logs, and backups. Patch .NET, Node, the renderer, browser kiosks, and the host OS on a controlled schedule.

The supplied Nginx file is an example, not a certificate automation policy. Review hostname, upload limit, trusted proxy ranges, logging, ciphers, and organisational retention requirements.

## Known limitations

- Single host and single active web process only; there is no horizontal scaling or distributed lock.
- SQLite and local content must not be placed on an unreliable/network filesystem.
- Device bearer tokens are long lived until revoked. A compromised kiosk profile can use its token; isolate the OS account and device.
- Cached content may continue playing after revocation, by design, but no new content can be fetched.
- OIDC claim mapping varies by identity provider and must be tested with real teacher/admin accounts.
- AD GUIDs prevent recycled usernames inheriting grants. AD account/password changes are checked at sign-in, not continuously; local disabling revokes existing app sessions. Direct LDAPS authentication is not MFA. See [AD setup and limits](active-directory.md).
- The in-process metrics counters reset at application restart; durable job/publication/device counts come from SQLite.
- Renderer fidelity and unsupported PowerPoint features are documented separately.
- Backups may contain school content and authentication/data-protection material; encrypt and access-control them at rest.
