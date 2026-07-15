-- Migration ownership is deliberately separate from every runtime identity.
DO $ownership$
DECLARE schema_name text;
DECLARE object_record record;
BEGIN
  FOREACH schema_name IN ARRAY ARRAY[
    'identity','journal','performance','discipline','reminder','market','market_data_public',
    'price_alert','rotation','stock_research','partner','content','operations'
  ] LOOP
    EXECUTE format('ALTER SCHEMA %I OWNER TO trade_diary_migrator', schema_name);

    FOR object_record IN
      SELECT c.relkind, c.relname
      FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
      WHERE n.nspname = schema_name AND c.relkind IN ('r','p','v','m','S','f')
    LOOP
      EXECUTE format(
        'ALTER %s %I.%I OWNER TO trade_diary_migrator',
        CASE object_record.relkind
          WHEN 'S' THEN 'SEQUENCE'
          WHEN 'v' THEN 'VIEW'
          WHEN 'm' THEN 'MATERIALIZED VIEW'
          WHEN 'f' THEN 'FOREIGN TABLE'
          ELSE 'TABLE'
        END,
        schema_name, object_record.relname
      );
    END LOOP;

    FOR object_record IN
      SELECT p.oid::regprocedure AS signature
      FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
      WHERE n.nspname = schema_name
    LOOP
      EXECUTE format('ALTER FUNCTION %s OWNER TO trade_diary_migrator', object_record.signature);
    END LOOP;
  END LOOP;
END $ownership$;

-- Start from no runtime access, so re-running this file also removes accidental grants.
DO $revoke$
DECLARE role_name text;
DECLARE schema_name text;
BEGIN
  FOREACH role_name IN ARRAY ARRAY[
    'identity_service','journal_service','performance_service','discipline_service',
    'reminder_service','market_data_service','price_alert_service','rotation_service',
    'stock_research_service','partner_service','content_service','operations_service'
  ] LOOP
    FOREACH schema_name IN ARRAY ARRAY[
      'identity','journal','performance','discipline','reminder','market','market_data_public',
      'price_alert','rotation','stock_research','partner','content','operations'
    ] LOOP
      EXECUTE format('REVOKE ALL ON SCHEMA %I FROM %I', schema_name, role_name);
      EXECUTE format('REVOKE ALL ON ALL TABLES IN SCHEMA %I FROM %I', schema_name, role_name);
      EXECUTE format('REVOKE ALL ON ALL SEQUENCES IN SCHEMA %I FROM %I', schema_name, role_name);
      EXECUTE format('REVOKE ALL ON ALL FUNCTIONS IN SCHEMA %I FROM %I', schema_name, role_name);
    END LOOP;
  END LOOP;
END $revoke$;

GRANT USAGE ON SCHEMA identity TO identity_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA identity TO identity_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA identity TO identity_service;
GRANT USAGE ON SCHEMA journal TO journal_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA journal TO journal_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA journal TO journal_service;
GRANT USAGE ON SCHEMA performance TO performance_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA performance TO performance_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA performance TO performance_service;
GRANT USAGE ON SCHEMA discipline TO discipline_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA discipline TO discipline_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA discipline TO discipline_service;
GRANT USAGE ON SCHEMA reminder TO reminder_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA reminder TO reminder_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA reminder TO reminder_service;
GRANT USAGE ON SCHEMA price_alert TO price_alert_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA price_alert TO price_alert_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA price_alert TO price_alert_service;
GRANT USAGE ON SCHEMA rotation TO rotation_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA rotation TO rotation_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA rotation TO rotation_service;
GRANT USAGE ON SCHEMA stock_research TO stock_research_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA stock_research TO stock_research_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA stock_research TO stock_research_service;
GRANT USAGE ON SCHEMA partner TO partner_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA partner TO partner_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA partner TO partner_service;
GRANT USAGE ON SCHEMA content TO content_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA content TO content_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA content TO content_service;
GRANT USAGE ON SCHEMA operations TO operations_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA operations TO operations_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA operations TO operations_service;

GRANT USAGE ON SCHEMA market TO market_data_service;
GRANT SELECT, INSERT, UPDATE, DELETE ON market.symbols, market.provider_runs, market.daily_bars TO market_data_service;
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA market TO market_data_service;
GRANT SELECT ON market.published_symbols_v1, market.published_daily_bars_v1, market.published_provider_health_v1 TO market_data_service;
GRANT USAGE ON SCHEMA market_data_public TO market_data_service;
GRANT SELECT ON market_data_public.adjusted_daily_bars_v1 TO market_data_service;

-- Cross-service market access is view-only. No consumer gets access to market base tables.
GRANT USAGE ON SCHEMA market TO price_alert_service;
GRANT SELECT ON market.published_provider_health_v1 TO price_alert_service;
GRANT USAGE ON SCHEMA market_data_public TO price_alert_service, rotation_service;
GRANT SELECT ON market_data_public.adjusted_daily_bars_v1 TO price_alert_service, rotation_service;

-- Future objects created by the migration owner inherit the same narrow runtime grants.
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA identity GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO identity_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA identity GRANT USAGE,SELECT ON SEQUENCES TO identity_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA journal GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO journal_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA journal GRANT USAGE,SELECT ON SEQUENCES TO journal_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA performance GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO performance_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA performance GRANT USAGE,SELECT ON SEQUENCES TO performance_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA discipline GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO discipline_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA discipline GRANT USAGE,SELECT ON SEQUENCES TO discipline_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA reminder GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO reminder_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA reminder GRANT USAGE,SELECT ON SEQUENCES TO reminder_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA price_alert GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO price_alert_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA price_alert GRANT USAGE,SELECT ON SEQUENCES TO price_alert_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA rotation GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO rotation_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA rotation GRANT USAGE,SELECT ON SEQUENCES TO rotation_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA stock_research GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO stock_research_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA stock_research GRANT USAGE,SELECT ON SEQUENCES TO stock_research_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA partner GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO partner_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA partner GRANT USAGE,SELECT ON SEQUENCES TO partner_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA content GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO content_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA content GRANT USAGE,SELECT ON SEQUENCES TO content_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA operations GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO operations_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA operations GRANT USAGE,SELECT ON SEQUENCES TO operations_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA market GRANT SELECT,INSERT,UPDATE,DELETE ON TABLES TO market_data_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA market GRANT USAGE,SELECT ON SEQUENCES TO market_data_service;
ALTER DEFAULT PRIVILEGES FOR ROLE trade_diary_migrator IN SCHEMA market_data_public GRANT SELECT ON TABLES TO market_data_service,price_alert_service,rotation_service;

REVOKE CREATE ON DATABASE trade_diary FROM identity_service,journal_service,performance_service,discipline_service,reminder_service,market_data_service,price_alert_service,rotation_service,stock_research_service,partner_service,content_service,operations_service;
REVOKE ALL ON SCHEMA platform_migrations FROM identity_service,journal_service,performance_service,discipline_service,reminder_service,market_data_service,price_alert_service,rotation_service,stock_research_service,partner_service,content_service,operations_service;
REVOKE ALL ON platform_migrations.schema_history FROM identity_service,journal_service,performance_service,discipline_service,reminder_service,market_data_service,price_alert_service,rotation_service,stock_research_service,partner_service,content_service,operations_service;
