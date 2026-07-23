# 数据库 ER 图说明

## 当前主线表

- `seafood_products`
- `seafood_traits`
- `model_versions`
- `channel_configs`

这 4 张表是当前系统主线，分别对应：

- 海鲜产品
- 产品性状
- 模型
- 通道

## 运行记录与辅助表

- `inspection_records`
- `alarm_events`
- `user_accounts`

## 当前关系

- `seafood_traits.seafood_product_id -> seafood_products.id`
- `channel_configs.seafood_product_id -> seafood_products.id`
- `channel_configs.model_version_id -> model_versions.id`
- `inspection_records.model_version_id -> model_versions.id`

## 说明

- `product_recipes` 和 `recipe_model_bindings` 已从当前系统主线中移除，数据库初始化时也会自动删除这两张旧表。
- `inspection_records` 里目前仍保留了 `recipe_id` 这个历史字段名，但它已不再对应独立的 `product_recipes` 概念，后续如有需要可以再做字段重命名迁移。
