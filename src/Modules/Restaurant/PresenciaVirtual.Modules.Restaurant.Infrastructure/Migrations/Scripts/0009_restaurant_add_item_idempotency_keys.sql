-- BR6: records which (order, menu item, quantity) a given Idempotency-Key applied, per tenant,
-- for AddItem specifically. Kept separate from Ordering's CreateOrder idempotency table
-- (restaurant.order_idempotency_keys) since the two capabilities claim different request
-- shapes and are scoped independently per BR6's (TenantId, IdempotencyKey) rule.

CREATE TABLE restaurant.add_item_idempotency_keys
(
    tenant_id       uuid NOT NULL,
    idempotency_key text NOT NULL,
    order_id        uuid NOT NULL,
    menu_item_id    uuid NOT NULL,
    quantity        integer NOT NULL,
    created_at      timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (tenant_id, idempotency_key)
);

ALTER TABLE restaurant.add_item_idempotency_keys ENABLE ROW LEVEL SECURITY;
ALTER TABLE restaurant.add_item_idempotency_keys FORCE ROW LEVEL SECURITY;

-- See 0001_restaurant_tables.sql for why NULLIF is required here.
CREATE POLICY tenant_isolation ON restaurant.add_item_idempotency_keys
    USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
