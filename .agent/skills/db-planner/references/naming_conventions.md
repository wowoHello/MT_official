# 資料庫命名規範

統一的命名規範對於資料庫的易讀性、可維護性和團隊協作至關重要。以下是建議的命名規範：

## 1. 資料表命名

- **原則**：使用小寫、單數名詞，並以底線 `_` 分隔單字 (snake_case)。應具備描述性，清晰表達資料表的內容。
- **範例**：`users`, `products`, `order_items`, `user_profiles`
- **避免**：`TBL_Users`, `ProductsTable`, `OrderItems_T`

## 2. 欄位命名

- **原則**：使用小寫、具描述性的名詞，並以底線 `_` 分隔單字 (snake_case)。應清晰表達欄位所儲存的資料內容。
- **範例**：`id`, `name`, `email`, `created_at`, `price`, `quantity`
- **主鍵**：建議使用 `id` 或 `[table_name]_id` (例如 `user_id`)。
- **外鍵**：建議使用 `[referenced_table_name]_id` (例如 `user_id` 參考 `users` 表的 `id`)。
- **布林值**：建議使用 `is_` 或 `has_` 開頭 (例如 `is_active`, `has_permission`)。
- **時間戳記**：建議使用 `created_at`, `updated_at`, `deleted_at`。
- **避免**：`UserID`, `product_name_string`, `active_flag`

## 3. 索引命名

- **原則**：建議以 `idx_` 開頭，後接資料表名稱和被索引的欄位名稱。對於唯一索引，可以使用 `uq_` 開頭。
- **範例**：`idx_users_email`, `uq_products_sku`

## 4. 視圖 (View) 命名

- **原則**：建議以 `v_` 開頭，後接具描述性的名稱。
- **範例**：`v_active_users`, `v_product_sales`

## 5. 儲存程序 (Stored Procedure) 命名

- **原則**：建議以 `sp_` 開頭，後接動詞和名詞，表示其操作。
- **範例**：`sp_get_user_by_id`, `sp_create_order`

## 6. 觸發器 (Trigger) 命名

- **原則**：建議以 `tr_` 開頭，後接資料表名稱、觸發時機 (before/after)、觸發事件 (insert/update/delete)。
- **範例**：`tr_users_before_insert`, `tr_products_after_update`

## 7. 註解

- **原則**：所有資料表和欄位都應提供清晰、簡潔的註解，說明其用途、資料型別、約束條件等。這對於資料庫的理解和維護至關重要。
