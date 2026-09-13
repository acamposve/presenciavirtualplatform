-- Grants the least-privilege application role (presenciavirtual_app, 0000_app_role.sql) the
-- access it needs for CreateMenuItem. 0010_add_item_grants.sql only granted SELECT on
-- restaurant.menu_items, since AddItem only ever reads it; this specification's INSERT needs
-- to be granted explicitly.

GRANT INSERT ON restaurant.menu_items TO presenciavirtual_app;
