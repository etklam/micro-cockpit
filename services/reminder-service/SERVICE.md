# Reminder Service

Owns `reminder.*` Diary Alerts, delivery attempts, and event inbox/outbox.

- Diary ownership is verified through Journal using the caller's Bearer token.
- Week repeats through Friday; Month repeats through month-end; both skip weekends.
- Each occurrence recomputes UTC from its IANA timezone wall clock.
- Worker claims use `FOR UPDATE SKIP LOCKED`; a unique occurrence key prevents double delivery.
- Delivery is in-app only. Price Alerts are a separate future service.
