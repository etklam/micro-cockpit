-- migration-id: 0015
-- owner: journal-service
-- description: Enforce diary review ownership integrity

ALTER TABLE journal.diaries
  ADD CONSTRAINT diaries_id_user_unique UNIQUE (id, user_id);

ALTER TABLE journal.diary_reviews
  ADD CONSTRAINT diary_reviews_diary_user_fk
  FOREIGN KEY (diary_id, user_id) REFERENCES journal.diaries(id, user_id);
