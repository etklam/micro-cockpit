-- Values are psql variables supplied from deployment environment by apply.sh.
SELECT format('ALTER ROLE %I PASSWORD %L', role_name, password)
FROM (VALUES
  ('trade_diary_migrator', :'migrator_password'),
  ('identity_service', :'identity_password'),
  ('journal_service', :'journal_password'),
  ('performance_service', :'performance_password'),
  ('discipline_service', :'discipline_password'),
  ('reminder_service', :'reminder_password'),
  ('market_data_service', :'market_data_password'),
  ('price_alert_service', :'price_alert_password'),
  ('rotation_service', :'rotation_password'),
  ('stock_research_service', :'stock_research_password'),
  ('partner_service', :'partner_password'),
  ('content_service', :'content_password'),
  ('operations_service', :'operations_password')
) credentials(role_name, password)
\gexec
