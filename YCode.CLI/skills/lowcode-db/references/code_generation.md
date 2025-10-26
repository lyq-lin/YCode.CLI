# JSON Schema 生成模式

## 完整数据库Schema示例

### 学生选课系统完整Schema

```json
{
  "database": {
    "name": "SchoolDB",
    "description": "学生选课系统数据库",
    "version": "1.0.0",
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
            },
            {
              "id": "gender",
              "widget": "field",
              "props": {
                "displayName": "性别",
                "name": "Gender",
                "dataType": "String",
                "required": true,
                "length": 2
              }
            },
            {
              "id": "enrollment_date",
              "widget": "field",
              "props": {
                "displayName": "入学日期",
                "name": "EnrollmentDate",
                "dataType": "DateTime",
                "required": false
              }
            }
          ]
        }
      },
      {
        "id": "course_table",
        "widget": "table",
        "props": {
          "displayName": "课程表",
          "name": "Course",
          "description": "存储课程信息",
          "fields": [
            {
              "id": "course_id",
              "widget": "field",
              "props": {
                "displayName": "课程ID",
                "name": "CourseId",
                "dataType": "Int32",
                "primaryKey": true,
                "identity": true
              }
            },
            {
              "id": "course_name",
              "widget": "field",
              "props": {
                "displayName": "课程名称",
                "name": "CourseName",
                "dataType": "String",
                "required": true,
                "length": 100
              }
            },
            {
              "id": "credit",
              "widget": "field",
              "props": {
                "displayName": "学分",
                "name": "Credit",
                "dataType": "Int32",
                "required": true,
                "defaultValue": 2
              }
            },
            {
              "id": "teacher_id",
              "widget": "field",
              "props": {
                "displayName": "教师ID",
                "name": "TeacherId",
                "dataType": "Int32",
                "required": false
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
          "foreignKey": "StudentId",
          "cascade": "Cascade"
        }
      },
      {
        "id": "course_student_relation",
        "widget": "relation",
        "props": {
          "type": "oneToMany",
          "sourceTable": "Course",
          "targetTable": "StudentCourse",
          "foreignKey": "CourseId",
          "cascade": "Cascade"
        }
      }
    ]
  }
}
```

## Schema 模板库

### 常用字段模板

**主键字段模板：**
```json
{
  "id": "primary_key_template",
  "widget": "field",
  "props": {
    "displayName": "ID",
    "name": "Id",
    "dataType": "Int32",
    "primaryKey": true,
    "identity": true,
    "seed": 1,
    "increment": 1
  }
}
```

**姓名字段模板：**
```json
{
  "id": "name_field_template",
  "widget": "field",
  "props": {
    "displayName": "姓名",
    "name": "Name",
    "dataType": "String",
    "required": true,
    "length": 50
  }
}
```

**时间字段模板：**
```json
{
  "id": "datetime_field_template",
  "widget": "field",
  "props": {
    "displayName": "时间",
    "name": "DateTime",
    "dataType": "DateTime",
    "required": false
  }
}
```

### 常用关系模板

**一对多关系模板：**
```json
{
  "id": "one_to_many_template",
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

## Schema 验证工具

### 结构验证示例

```javascript
// Schema 验证函数示例
function validateSchema(schema) {
  const errors = [];

  // 验证widget类型
  if (!['table', 'field', 'relation'].includes(schema.widget)) {
    errors.push(`Invalid widget type: ${schema.widget}`);
  }

  // 验证props存在性
  if (!schema.props) {
    errors.push('Missing props object');
  }

  // 验证id唯一性
  if (!schema.id || typeof schema.id !== 'string') {
    errors.push('Invalid or missing id');
  }

  return errors;
}
```

### 数据类型验证

```javascript
// 数据类型验证函数
function validateDataType(dataType, value) {
  switch (dataType) {
    case 'String':
      return typeof value === 'string';
    case 'Int32':
      return Number.isInteger(value) && value >= -2147483648 && value <= 2147483647;
    case 'DateTime':
      return value instanceof Date || !isNaN(Date.parse(value));
    case 'Decimal':
      return !isNaN(parseFloat(value));
    default:
      return false;
  }
}
```

## Schema 生成最佳实践

### 命名规范
- 使用有意义的id命名
- displayName使用中文描述
- name使用英文驼峰命名
- 保持命名一致性

### 结构组织
- 使用嵌套结构组织相关字段
- 保持Schema层级清晰
- 使用注释说明复杂配置

### 约束定义
- 明确定义所有必要的约束
- 使用默认值简化配置
- 考虑业务逻辑约束