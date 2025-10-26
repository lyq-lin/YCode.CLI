# JSON Schema 结构分析

## Schema 详细说明

### 表定义Schema结构

**表定义Schema：**
```json
{
  "id": "table_unique_id",
  "widget": "table",
  "props": {
    "displayName": "表显示名称",
    "name": "表名",
    "description": "表描述",
    "fields": [
      // 字段定义数组
    ]
  }
}
```

**Schema属性分析：**
- **id**: 唯一标识符，用于组件引用
- **widget**: 组件类型标识，固定为"table"
- **props**: 配置属性对象
- **displayName**: 用户可见的显示名称
- **name**: 实际表名
- **description**: 表功能描述
- **fields**: 嵌套的字段定义数组

### 字段定义Schema结构

**字段定义Schema：**
```json
{
  "id": "field_unique_id",
  "widget": "field",
  "props": {
    "displayName": "字段显示名",
    "name": "字段名",
    "dataType": "数据类型",
    "required": true,
    "length": 50,
    "defaultValue": "默认值",
    "primaryKey": false,
    "identity": false
  }
}
```

**字段属性分析：**
- **dataType**: 数据类型（String, Int32, DateTime, Decimal等）
- **required**: 是否必填字段
- **length**: 字符串字段长度限制
- **defaultValue**: 字段默认值
- **primaryKey**: 是否为主键
- **identity**: 是否为自增字段

### 关系定义Schema结构

**关系定义Schema：**
```json
{
  "id": "relation_unique_id",
  "widget": "relation",
  "props": {
    "type": "关系类型",
    "sourceTable": "源表名",
    "targetTable": "目标表名",
    "foreignKey": "外键字段名",
    "cascade": "级联操作"
  }
}
```

**关系属性分析：**
- **type**: 关系类型（oneToMany, manyToMany, oneToOne）
- **sourceTable**: 关系源表
- **targetTable**: 关系目标表
- **foreignKey**: 外键字段名称
- **cascade**: 级联操作类型

## Schema 关系模型

### 关系类型Schema定义

**一对多关系Schema：**
```json
{
  "id": "one_to_many_relation",
  "widget": "relation",
  "props": {
    "type": "oneToMany",
    "sourceTable": "源表名",
    "targetTable": "目标表名",
    "foreignKey": "外键字段名",
    "cascade": "Cascade"
  }
}
```

**多对多关系Schema：**
```json
{
  "id": "many_to_many_relation",
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
```

### Schema 完整性约束

**引用完整性：**
- 通过relation schema定义外键关系
- 支持级联操作配置
- 确保数据一致性

**唯一性约束：**
- 通过字段schema的unique属性定义
- 支持复合唯一约束
- 业务逻辑约束实现

## Schema 数据类型分析

### 支持的数据类型
- **String**: 字符串类型，支持length属性
- **Int32**: 32位整数类型
- **DateTime**: 日期时间类型
- **Decimal**: 小数类型，支持精度配置
- **Boolean**: 布尔类型

### 数据类型属性
- **required**: 字段必填性
- **defaultValue**: 默认值设置
- **length**: 字符串长度限制
- **precision**: 小数精度设置

## Schema 验证策略

### 结构验证
- 验证widget类型有效性
- 验证props属性完整性
- 验证嵌套结构正确性

### 业务验证
- 验证数据类型一致性
- 验证约束规则有效性
- 验证关系配置正确性