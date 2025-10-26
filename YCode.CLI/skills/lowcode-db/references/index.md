# LowCode DB JSON Schema 参考文档索引

## 核心文档

### [SKILL.md](../SKILL.md)
- 主要技能文档
- JSON Schema快速参考指南
- 低代码数据库私有协议说明

### [schema_analysis.md](./schema_analysis.md)
- JSON Schema结构详细分析
- 表定义Schema说明
- 字段和关系Schema分析
- Schema验证规则

### [code_generation.md](./code_generation.md)
- JSON Schema生成模式
- Schema模板示例
- 完整数据库Schema示例
- Schema验证工具

### [business_rules.md](./business_rules.md)
- Schema约束规则
- 数据验证规则
- 业务逻辑Schema定义
- 关系约束Schema

## JSON Schema 核心结构

### 表定义Schema
- **widget**: "table" - 标识表组件
- **props**: 包含表的基本配置
- **fields**: 嵌套字段定义数组

### 字段定义Schema
- **widget**: "field" - 标识字段组件
- **props**: 包含字段属性和约束
- **dataType**: 数据类型定义
- **constraints**: 字段约束配置

### 关系定义Schema
- **widget**: "relation" - 标识关系组件
- **props**: 包含关系类型和配置
- **type**: 关系类型（oneToMany, manyToMany等）

## Schema 生成模式

### 完整数据库Schema
- 包含所有表、字段和关系定义
- 支持嵌套结构组织
- 统一的配置格式

### Schema 模板
- 可复用的Schema组件
- 标准化的配置模式
- 模块化设计支持

### Schema 验证
- 结构完整性验证
- 数据类型验证
- 约束规则验证

## Schema 约束规则

### 数据完整性
- 主键约束Schema定义
- 外键约束Schema定义
- 唯一性约束Schema定义

### 业务逻辑
- 字段验证规则Schema
- 关系约束Schema
- 业务状态Schema

### 验证规则
- 数据类型Schema验证
- 长度约束Schema验证
- 格式验证Schema

## Schema 扩展建议

### 结构优化
- 模块化Schema设计
- 可复用组件模板
- 版本管理支持

### 功能扩展
- 自定义数据类型支持
- 复杂验证规则
- 权限控制Schema

## 使用指南

### 快速开始
1. 阅读SKILL.md了解JSON Schema技能概述
2. 查看schema_analysis.md理解Schema结构
3. 使用code_generation.md生成Schema模板
4. 参考business_rules.md确保约束规则正确

### 开发建议
- 遵循统一的Schema结构规范
- 使用语义化命名约定
- 实施Schema验证流程
- 维护Schema版本历史

## 技术支持

如需进一步技术支持或Schema定制，请参考相关技术文档或联系开发团队。