-- Run after schema migrations as a database owner. Passwords are supplied by deployment, never stored here.
DO $roles$
DECLARE role_name text;
BEGIN
  FOREACH role_name IN ARRAY ARRAY['identity_service','journal_service','performance_service','discipline_service','reminder_service','market_data_service','price_alert_service','rotation_service','stock_research_service','partner_service','content_service','operations_service','tool_service']
  LOOP
    IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname=role_name) THEN
      EXECUTE format('CREATE ROLE %I NOLOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT',role_name);
    END IF;
  END LOOP;
END $roles$;

REVOKE CREATE ON SCHEMA public FROM PUBLIC;

GRANT USAGE, CREATE ON SCHEMA identity TO identity_service;
GRANT USAGE, CREATE ON SCHEMA journal TO journal_service;
GRANT USAGE, CREATE ON SCHEMA performance TO performance_service;
GRANT USAGE, CREATE ON SCHEMA discipline TO discipline_service;
GRANT USAGE, CREATE ON SCHEMA reminder TO reminder_service;
GRANT USAGE, CREATE ON SCHEMA market, market_data_public TO market_data_service;
GRANT USAGE, CREATE ON SCHEMA price_alert TO price_alert_service;
GRANT USAGE, CREATE ON SCHEMA rotation TO rotation_service;
GRANT USAGE, CREATE ON SCHEMA stock_research TO stock_research_service;
GRANT USAGE, CREATE ON SCHEMA partner TO partner_service;
GRANT USAGE, CREATE ON SCHEMA content TO content_service;
GRANT USAGE, CREATE ON SCHEMA operations TO operations_service;

GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA identity TO identity_service;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA journal TO journal_service;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA performance TO performance_service;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA discipline TO discipline_service;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA reminder TO reminder_service;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA market,market_data_public TO market_data_service;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA price_alert TO price_alert_service;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA rotation TO rotation_service;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA stock_research TO stock_research_service;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA partner TO partner_service;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA content TO content_service;
GRANT ALL PRIVILEGES ON ALL TABLES IN SCHEMA operations TO operations_service;

GRANT USAGE ON SCHEMA market_data_public TO price_alert_service,rotation_service;
GRANT SELECT ON market_data_public.adjusted_daily_bars_v1 TO price_alert_service,rotation_service;

-- Apply the same owner grants to future objects created by each role.
ALTER DEFAULT PRIVILEGES FOR ROLE identity_service IN SCHEMA identity GRANT ALL ON TABLES TO identity_service;
ALTER DEFAULT PRIVILEGES FOR ROLE journal_service IN SCHEMA journal GRANT ALL ON TABLES TO journal_service;
ALTER DEFAULT PRIVILEGES FOR ROLE performance_service IN SCHEMA performance GRANT ALL ON TABLES TO performance_service;
ALTER DEFAULT PRIVILEGES FOR ROLE discipline_service IN SCHEMA discipline GRANT ALL ON TABLES TO discipline_service;
ALTER DEFAULT PRIVILEGES FOR ROLE reminder_service IN SCHEMA reminder GRANT ALL ON TABLES TO reminder_service;
ALTER DEFAULT PRIVILEGES FOR ROLE market_data_service IN SCHEMA market,market_data_public GRANT ALL ON TABLES TO market_data_service;
ALTER DEFAULT PRIVILEGES FOR ROLE price_alert_service IN SCHEMA price_alert GRANT ALL ON TABLES TO price_alert_service;
ALTER DEFAULT PRIVILEGES FOR ROLE rotation_service IN SCHEMA rotation GRANT ALL ON TABLES TO rotation_service;
ALTER DEFAULT PRIVILEGES FOR ROLE stock_research_service IN SCHEMA stock_research GRANT ALL ON TABLES TO stock_research_service;
ALTER DEFAULT PRIVILEGES FOR ROLE partner_service IN SCHEMA partner GRANT ALL ON TABLES TO partner_service;
ALTER DEFAULT PRIVILEGES FOR ROLE content_service IN SCHEMA content GRANT ALL ON TABLES TO content_service;
ALTER DEFAULT PRIVILEGES FOR ROLE operations_service IN SCHEMA operations GRANT ALL ON TABLES TO operations_service;
