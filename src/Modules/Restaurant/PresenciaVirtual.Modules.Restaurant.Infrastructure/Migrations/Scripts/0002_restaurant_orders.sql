-- Ordering / CreateOrder (specs/restaurant/ordering/create-order.md). Only the "Open" status
-- exists so far; the CHECK constraint below will be widened when AddItem/CloseOrder/CancelOrder
-- introduce further statuses.

CREATE TABLE restaurant.orders
(
    id                 uuid PRIMARY KEY,
    tenant_id          uuid NOT NULL,
    table_id           uuid NOT NULL REFERENCES restaurant.tables (id),
    status             text NOT NULL CHECK (status = 'Open'),
    created_at         timestamptz NOT NULL,
    created_by_user_id uuid NOT NULL
);

CREATE INDEX ix_restaurant_orders_tenant_id ON restaurant.orders (tenant_id);

-- BR2: a table has at most one Open order at any time. This is the authoritative guarantee;
-- the application performs a pre-check for a friendlier error, but this index is what makes
-- it correct under concurrent requests.
CREATE UNIQUE INDEX ux_restaurant_orders_open_per_table
    ON restaurant.orders (tenant_id, table_id)
    WHERE status = 'Open';

ALTER TABLE restaurant.orders ENABLE ROW LEVEL SECURITY;
ALTER TABLE restaurant.orders FORCE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation ON restaurant.orders
    USING (tenant_id = current_setting('app.tenant_id', true)::uuid);
