# EnterpriseAccountingIntegration-Example

An event-driven example that sends hotel invoices to an accounting system, built with .NET 10, Keycloak and RabbitMQ.

A user signs in with Keycloak and submits an invoice to the API. The API checks the user's token and hotel, then publishes an event to RabbitMQ. A separate worker transfers the invoice to a simulated ERP. Failed transfers are tried up to three times, 5 seconds apart, and then move to a dead-letter queue.

```
User ── sign-in ──> Keycloak
  │
  └── JWT ──> Accounting.Api ──> RabbitMQ ──> Accounting.Worker ──> ERP (simulated)
```

## Set up

You need [Docker Desktop](https://www.docker.com/products/docker-desktop/) (or Docker with Compose v2) running, and ports 5080, 8080, 5672 and 15672 free.

1. Get the code and open its folder:

   ```bash
   git clone <repository-url>
   ```

2. Start everything from the folder that contains `docker-compose.yml`:

   ```bash
   docker compose up -d --build
   ```

   The first run can take a few minutes. Keycloak's realm and the RabbitMQ settings are set up automatically.

3. Check that all four services are running:

   ```bash
   docker compose ps
   ```

## Try it

Open http://localhost:5080, select **Sign in with Keycloak**, and sign in as a demo user. Then run a scenario from the page, such as **Send invoice to the queue** or **ERP stays down**.

All demo users have the password `accountant-dev-password`:

| User | Hotel | Can submit invoices |
| --- | --- | --- |
| `accountant` | `Hotel_01` | Yes |
| `accountant2` | `Hotel_02` | Yes |
| `auditor` | `Hotel_01` | No |

To see the worker's messages, run:

```bash
docker compose logs worker
```

| Tool | URL | Sign-in |
| --- | --- | --- |
| RabbitMQ management | http://localhost:15672 | `accounting` / `accounting-dev-password` |
| Keycloak admin | http://localhost:8080 | `admin` / `admin` |

These credentials are for this local demo only.

### Test with cURL

Run these in a bash terminal. On Windows, use Git Bash or WSL.

```bash
TOKEN=$(curl -s http://localhost:8080/realms/accounting-hub/protocol/openid-connect/token \
  -d grant_type=password \
  -d client_id=accounting-hub-client \
  -d username=accountant \
  -d password=accountant-dev-password \
  | sed -E 's/.*"access_token":"([^"]+)".*/\1/')
```

```bash
curl -i http://localhost:5080/api/v1/accounting/invoices/sync \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"invoiceId":"INV-999","tenantId":"Hotel_01","amount":1250.00,"erpSystem":"Logo/Netsis"}'
```

The API returns `202 Accepted`. To use Postman instead, choose **OAuth 2.0** with the `Password Credentials` grant, the token URL above, the same client ID and user, and no client secret.

## Stop

```bash
docker compose down -v
```

This removes the containers and any queued messages.

## Limitations

This is a local demo, not a production setup. RabbitMQ runs on one node, so its quorum queues don't replicate here, and Keycloak runs in development mode. Invoice status shown on the page is kept in memory and is lost when the API restarts.
