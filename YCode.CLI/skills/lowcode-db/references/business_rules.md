# JSON Schema 约束规则分析

## Schema 完整性规则

### 主键约束Schema

**规则1：唯一标识符Schema**
- 主键字段必须定义primaryKey: true
- 自增字段必须定义identity: true
- 主键字段不允许为空

```json
{
  "id": "primary_key_field",
  "widget": "field",
  "props": {
    "primaryKey": true,
    "identity": true,
    "dataType": "Int32",
    "seed": 1,
    "increment": 1
  }
}
```

### 外键约束Schema

**规则2：引用完整性Schema**
- 通过relation schema定义外键关系
- 支持级联操作配置
- 确保数据类型一致性

```json
{
  "id": "foreign_key_relation",
  "widget": "relation",
  "props": {
    "type": "oneToMany",
    "sourceTable": "源表",
    "targetTable": "目标表",
    "foreignKey": "外键字段",
    "cascade": "Cascade"
  }
}
```

### 唯一性约束Schema

**规则3：业务唯一性Schema**
- 通过unique属性定义唯一约束
- 支持复合唯一约束
- 业务逻辑约束实现

```json
{
  "id": "unique_constraint_field",
  "widget": "field",
  "props": {
    "unique": true,
    "dataType": "Int32",
    "required": false
  }
}
```

## Schema 验证规则

### 字段验证Schema

**规则4：必填字段验证**
- 通过required属性定义必填字段
- 必填字段必须有默认值或用户输入

```json
{
  "id": "required_field",
  "widget": "field",
  "props": {
    "required": true,
    "dataType": "String",
    "length": 50
  }
}
```

**规则5：长度验证Schema**
- 字符串字段必须定义length属性
- 支持不同长度的字符串字段

```json
{
  "id": "length_validation_field",
  "widget": "field",
  "props": {
    "dataType": "String",
    "length": 100,
    "required": true
  }
}
```

### 数据类型验证Schema

**规则6：数据类型验证**
- 确保dataType属性值有效
- 支持标准数据类型集合

```json
{
  "id": "data_type_validation",
  "widget": "field",
  "props": {
    "dataType": "Int32",
    "required": true
  }
}
```

**规则7：默认值Schema**
- 通过defaultValue属性定义默认值
- 支持不同数据类型的默认值

```json
{
  "id": "default_value_field",
  "widget": "field",
  "props": {
    "dataType": "String",
    "defaultValue": "在职",
    "required": true
  }
}
```

## 关系约束Schema

### 一对多关系Schema

**规则8：一对多关系约束**
- 明确定义sourceTable和targetTable
- 配置适当的级联操作

```json
{
  "id": "one_to_many_constraint",
  "widget": "relation",
  "props": {
    "type": "oneToMany",
    "sourceTable": "源表",
    "targetTable": "目标表",
    "foreignKey": "外键字段",
    "cascade": "Cascade"
  }
}
```

### 多对多关系Schema

**规则9：多对多关系约束**
- 必须定义junctionTable
- 明确定义sourceKey和targetKey

```json
{
  "id": "many_to_many_constraint",
  "widget": "relation",
  "props": {
    "type": "manyToMany",
    "sourceTable": "源表",
    "targetTable": "目标表",
    "junctionTable": "连接表",
    "sourceKey": "源表外键",
    "targetKey": "目标表外键"
  }
}
```

## Schema 业务规则

### 业务状态Schema

**规则10：状态字段Schema**
- 状态字段必须有默认值
- 支持有限的状态值集合

```json
{
  "id": "status_field",
  "widget": "field",
  "props": {
    "displayName": "状态",
    "name": "Status",
    "dataType": "String",
    "required": true,
    "defaultValue": "在职",
    "length": 10
  }
}
```

### 格式验证Schema

**规则11：邮箱格式验证**
- 通过format属性定义格式验证
- 支持常见格式验证规则

```json
{
  "id": "email_field",
  "widget": "field",
  "props": {
    "displayName": "邮箱",
    "name": "Email",
    "dataType": "String",
    "required": false,
    "length": 100,
    "format": "email"
  }
}
```

## Schema 扩展性规则

### 模块化Schema设计

**规则12：组件复用**
- 创建可复用的字段模板
- 支持模块化Schema组织

**规则13：版本管理**
- 为Schema添加版本信息
- 支持Schema演化管理

### 约束扩展Schema

**规则14：自定义约束**
- 支持自定义验证规则
- 扩展业务逻辑约束

```json
{
  "id": "custom_constraint_field",
  "widget": "field",
  "props": {
    "dataType": "Int32",
    "required": true,
    "customValidation": {
      "min": 0,
      "max": 100
    }
  }
}
```

## Schema 验证策略

### 结构验证规则

**规则15：Schema完整性**
- 验证所有必需属性存在
- 确保嵌套结构正确

**规则16：数据类型一致性**
- 验证字段数据类型有效性
- 确保关系数据类型匹配

### 业务验证规则

**规则17：业务逻辑验证**
- 验证业务约束有效性
- 确保关系配置合理

**规则18：命名规范验证**
- 验证命名规范一致性
- 确保标识符唯一性