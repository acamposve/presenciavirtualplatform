-- Grants the least-privilege application role (presenciavirtual_app, 0000_app_role.sql) the
-- access it needs for AddItem. Schema-level USAGE was already granted in 0004_grants.sql.

GRANT SELECT ON restaurant.menu_items TO presenciavirtual_app;
GRANT SELECT ON restaurant.settings TO presenciavirtual_app;
GRANT SELECT, INSERT, UPDATE ON restaurant.order_items TO presenciavirtual_app;
GRANT SELECT, INSERT ON restaurant.add_item_idempotency_keys TO presenciavirtual_app;
