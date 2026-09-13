-- Grants the least-privilege application role (presenciavirtual_app, 0000_app_role.sql) the
-- access it needs for UpdateRestaurantSettings. 0010_add_item_grants.sql only granted SELECT on
-- restaurant.settings, since AddItem only reads it.

GRANT INSERT, UPDATE ON restaurant.settings TO presenciavirtual_app;
