# Caddy with the Cloudflare DNS plugin so it can solve the ACME DNS-01 challenge and obtain a Let's
# Encrypt WILDCARD certificate for *.pos.corebalt.co.ke (Cloudflare free Universal SSL does NOT cover a
# second-level wildcard, so TLS is terminated here at the origin instead).
FROM caddy:2-builder AS builder
RUN xcaddy build --with github.com/caddy-dns/cloudflare

FROM caddy:2
COPY --from=builder /usr/bin/caddy /usr/bin/caddy
