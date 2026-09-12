-- Grants the least-privilege application role (presenciavirtual_app, created in
-- 0000_app_role.sql) the access it needs to Restaurant's own tables. RLS policies on each
-- table (0001-0003) then constrain what rows this role can actually see/affect per tenant.

GRANT USAGE ON SCHEMA restaurant TO presenciavirtual_app;

GRANT SELECT, INSERT, UPDATE, DELETE
    ON restaurant.tables, restaurant.orders, restaurant.order_idempotency_keys
    TO presenciavirtual_app;
