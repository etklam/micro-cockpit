-- migration-id: 0017
-- owner: price-alert-service
-- description: Daily bar evaluation price and trigger observation compatibility

ALTER TABLE price_alert.alerts
  ADD COLUMN evaluation_price text NOT NULL DEFAULT 'close',
  ADD CONSTRAINT price_alert_evaluation_price_check
    CHECK (evaluation_price IN ('open','close'));

ALTER TABLE price_alert.triggers
  ADD COLUMN observed_price numeric(20,8),
  ADD COLUMN price_type text;

UPDATE price_alert.triggers
SET observed_price = observed_close,
    price_type = 'close';

ALTER TABLE price_alert.triggers
  ALTER COLUMN observed_price SET NOT NULL,
  ALTER COLUMN price_type SET NOT NULL,
  ADD CONSTRAINT price_alert_trigger_price_type_check
    CHECK (price_type IN ('open','close'));
