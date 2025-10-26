---
name: lowcode-db
description: 低代码数据库JSON Schema设计技能，专门用于创建和解析低代码平台的数据库私有协议Schema，支持快速构建数据模型。建议一张表就构建一个todo待办事项，以降低单次会话中超出最大输出限制和提高用户的等待体验。
---

# LowCode DB JSON Schema Skill

专门用于低代码数据库JSON Schema设计和分析的技能，专注于创建符合低代码平台私有协议的数据库Schema定义。

## When to Use This Skill

此技能应在以下情况下使用：
- 设计和创建低代码平台的JSON Schema
- 解析和理解低代码数据库私有协议
- 构建符合特定格式的数据模型定义
- 验证JSON Schema的结构和完整性
- 生成低代码平台可用的数据库配置

## Quick Reference

### JSON Schema 核心结构

**表定义Schema**
```json
{
  "id": "table_id",
  "widget": "table",
  "props": {
    "displayName": "表显示名称",
    "name": "表名",
    "description": "表描述"
  }
}
```

**字段定义Schema**
```json
{
  "id": "field_id",
  "widget": "field",
  "props": {
    "displayName": "字段显示名",
    "name": "字段名",
    "dataType": "数据类型",
    "required": true,
    "length": 50,
    "defaultValue": "默认值"
  }
}
```

**关系定义Schema**
```json
{
  "id": "relation_id",
  "widget": "relation",
  "props": {
    "type": "oneToMany",
    "sourceTable": "源表",
    "targetTable": "目标表",
    "foreignKey": "外键字段"
  }
}
```

### 核心关系模式

```json
// 一对多关系Schema
{
  "id": "relation_one_to_many",
  "widget": "relation",
  "props": {
    "type": "oneToMany",
    "sourceTable": "源表名",
    "targetTable": "目标表名",
    "foreignKey": "外键字段名",
    "cascade": "级联操作类型"
  }
}

// 多对多关系Schema
{
  "id": "relation_many_to_many",
  "widget": "relation",
  "props": {
    "type": "manyToMany",
    "sourceTable": "源表名",
    "targetTable": "目标表名",
    "junctionTable": "连接表名",
    "sourceKey": "源表外键",
    "targetKey": "目标表外键"
  }
}

// 一对一关系Schema
{
  "id": "relation_one_to_one",
  "widget": "relation",
  "props": {
    "type": "oneToOne",
    "sourceTable": "源表名",
    "targetTable": "目标表名",
    "foreignKey": "外键字段名"
  }
}
```

## JSON Schema Analysis

### Schema 结构分析

**核心Schema类型：**
- **Table Schema**：表定义Schema，包含表的基本信息
- **Field Schema**：字段定义Schema，定义字段属性和约束
- **Relation Schema**：关系定义Schema，描述表间关系
- **Index Schema**：索引定义Schema，优化查询性能

**Schema层级结构：**
- **Root Level**：数据库整体配置
- **Table Level**：单个表定义
- **Field Level**：表内字段定义
- **Relation Level**：表间关系定义

### Schema 详细分析

#### 表定义Schema示例
```json
{
  "id": "student_table",
  "widget": "table",
  "props": {
    "displayName": "学生表",
    "name": "Student",
    "description": "存储学生基本信息",
    "fields": [
      {
        "id": "student_id",
        "widget": "field",
        "props": {
          "displayName": "学生ID",
          "name": "StudentId",
          "dataType": "Int32",
          "primaryKey": true,
          "identity": true,
          "seed": 1,
          "increment": 1
        }
      },
      {
        "id": "student_name",
        "widget": "field",
        "props": {
          "displayName": "学生姓名",
          "name": "StudentName",
          "dataType": "String",
          "required": true,
          "length": 50
        }
      }
    ]
  }
}
```

**Schema设计特点：**
- 使用标准化的widget类型区分不同组件
- props对象包含所有配置属性
- 支持嵌套结构定义字段和关系
- 统一的id命名规范

## JSON Schema 约束分析

### Schema 完整性约束

1. **主键约束**：通过`primaryKey: true`和`identity: true`定义
2. **外键约束**：通过relation schema的foreignKey属性定义
3. **唯一约束**：通过index schema的unique属性定义
4. **默认值约束**：通过defaultValue属性定义

### Schema 验证规则

1. **数据类型验证**：确保dataType属性值有效
2. **长度验证**：验证字符串字段的length属性
3. **必填验证**：通过required属性控制字段必填性
4. **格式验证**：验证email、phone等特殊格式字段

## JSON Schema 生成模式

### 完整数据库Schema示例

```json
{
  "database": {
    "name": "SchoolDB",
    "description": "学生选课系统数据库",
    "tables": [
      {
        "id": "student_table",
        "widget": "table",
        "props": {
          "displayName": "学生表",
          "name": "Student",
          "description": "存储学生基本信息",
          "fields": [
            {
              "id": "student_id",
              "widget": "field",
              "props": {
                "displayName": "学生ID",
                "name": "StudentId",
                "dataType": "Int32",
                "primaryKey": true,
                "identity": true
              }
            },
            {
              "id": "student_name",
              "widget": "field",
              "props": {
                "displayName": "学生姓名",
                "name": "StudentName",
                "dataType": "String",
                "required": true,
                "length": 50
              }
            }
          ]
        }
      }
    ],
    "relations": [
      {
        "id": "student_course_relation",
        "widget": "relation",
        "props": {
          "type": "oneToMany",
          "sourceTable": "Student",
          "targetTable": "StudentCourse",
          "foreignKey": "StudentId"
        }
      }
    ]
  }
}
```

## Schema 扩展建议

### Schema 结构优化
1. **模块化设计**：将大型Schema拆分为多个模块
2. **复用组件**：创建可复用的字段和关系模板
3. **版本控制**：为Schema添加版本信息便于管理
4. **文档注释**：在Schema中添加详细的注释说明

### 功能扩展
1. **自定义数据类型**：支持扩展自定义数据类型
2. **验证规则扩展**：添加复杂的业务验证规则
3. **索引策略**：支持复杂的索引配置
4. **权限控制**：在Schema中定义数据访问权限

## 使用指南

### JSON Schema 设计原则
1. **标准化结构**：遵循统一的Schema结构规范
2. **语义化命名**：使用有意义的id和displayName
3. **约束完整性**：确保所有必要的约束都已定义
4. **可读性优先**：保持Schema结构清晰易读

### 开发建议
1. **Schema验证**：在生成前验证Schema的完整性
2. **模板复用**：创建常用Schema模板库
3. **版本管理**：为Schema变化维护版本历史
4. **文档生成**：从Schema自动生成技术文档

这个JSON Schema设计专注于低代码数据库私有协议的创建，支持快速构建和验证数据模型定义。