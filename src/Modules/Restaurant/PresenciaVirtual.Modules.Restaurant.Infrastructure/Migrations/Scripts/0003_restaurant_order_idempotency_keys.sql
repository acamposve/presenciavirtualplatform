-- BR6/AC7/AC8: records which order a given Idempotency-Key produced, per tenant, so a
-- replayed CreateOrder request returns the original order instead of creating a duplicate,
-- and a key reused with a different table is rejected as a conflict.

CREATE TABLE restaurant.order_idempotency_keys
(
    tenant_id       uuid NOT NULL,
    idempotency_key text NOT NULL,
    table_id        uuid NOT NULL,
    order_id        uuid NOT NULL REFERENCES restaurant.orders (id),
    created_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, idempotency_key)
);

ALTER TABLE restaurant.order_idempotency_keys ENABLE ROW LEVEL SECURITY;
ALTER TABLE restaurant.order_idempotency_keys FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON restaurant.order_idempotency_keys
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
