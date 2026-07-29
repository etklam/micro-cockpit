-- migration-id: 0041
-- owner: platform
-- description: Remove unlaunched legacy product schemas and references
-- destructive-approved: market-observation-cutover-unlaunched

DROP TABLE journal.diary_tags;
DROP TABLE journal.diary_reviews;
DROP TABLE journal.transactions;
DROP TABLE journal.diaries;

ALTER TABLE tool.saved_calculations
    DROP COLUMN source_transaction_id,
    DROP COLUMN source_diary_id;

DROP SCHEMA performance CASCADE;
DROP SCHEMA discipline CASCADE;
DROP SCHEMA reminder CASCADE;
DROP SCHEMA stock_research CASCADE;
DROP SCHEMA price_alert CASCADE;
DROP SCHEMA rotation CASCADE;
DROP SCHEMA partner CASCADE;
DROP SCHEMA content CASCADE;
DROP SCHEMA operations CASCADE;
