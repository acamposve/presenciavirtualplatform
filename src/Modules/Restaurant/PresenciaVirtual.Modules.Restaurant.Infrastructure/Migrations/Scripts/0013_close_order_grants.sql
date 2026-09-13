-- Grants the least-privilege application role (presenciavirtual_app, 0000_app_role.sql) the
-- access it needs for CloseOrder. UPDATE on restaurant.orders was already granted in
-- 0004_grants.sql; schema-level USAGE was also already granted there.

GRANT SELECT, INSERT ON restaurant.close_order_idempotency_keys TO presenciavirtual_app;
