# 位置切换功能 - 需求文档

## Introduction

参考美团 App 左上角「当前城市」体验，在息县农贸通中引入「当前位置（乡镇）」概念：用户在首页左上角可查看并切换当前乡镇，选择结果在本地持久化，并联动首页推荐与市场列表的筛选，同时作为发布供应/采购时的默认产地/交货地。本功能基于既有 `sys_region` 行政层级（县-乡镇-村），不引入 GPS 定位。

## Glossary

- **当前位置**：用户选择的乡镇（可选到村）及其名称，存储于客户端本地（`uni.setStorageSync`）。
- **全部乡镇**：当前位置为空时的默认状态，表示不做乡镇级过滤（查看全县）。
- **RegionPicker**：区域选择底部弹层组件，`frontend/src/components/region-picker.vue`。
- **乡镇**：`sys_region.region_level = 4` 的记录，父级为息县（county_id = 3）。
- **村级**：`sys_region.region_level = 5` 的记录，父级为乡镇。

## Requirements

### Requirement 1 当前位置的展示与切换

**User Story:** AS 用户, I want 在首页左上角看到并切换当前乡镇, so that 我能聚焦自己关心的区域。

#### Acceptance Criteria

1. WHEN 用户进入首页, 系统 SHALL 在页面左上区域展示「当前位置」入口，显示当前乡镇名称或「全部乡镇」。
2. WHEN 用户点击「当前位置」入口, 系统 SHALL 打开区域选择弹层展示息县全部乡镇。
3. WHEN 用户在弹层中选择某乡镇, 系统 SHALL 关闭弹层并更新首页左上角显示为该乡镇名称。
4. WHERE 弹层允许清空位置, WHEN 用户点击「全部乡镇」选项, 系统 SHALL 将当前位置置为空并显示「全部乡镇」。
5. WHEN 用户切换当前位置, 系统 SHALL 将「当前位置」写入客户端本地存储，供后续启动读取。

### Requirement 2 首页内容按位置联动

**User Story:** AS 用户, I want 首页推荐供应与采购按当前乡镇过滤, so that 我优先看到附近信息。

#### Acceptance Criteria

1. WHILE 当前位置为某乡镇, WHEN 首页加载供应/采购列表, 系统 SHALL 仅请求该乡镇（含其下村庄）的信息。
2. WHILE 当前位置为空, WHEN 首页加载供应/采购列表, 系统 SHALL 请求全县信息且不附加乡镇条件。
3. WHEN 用户在首页切换当前位置, 系统 SHALL 重新加载首页供应与采购列表。

### Requirement 3 市场列表按位置联动

**User Story:** AS 用户, I want 市场列表默认按当前位置筛选且仍可切换, so that 浏览与全局查看都方便。

#### Acceptance Criteria

1. WHEN 市场页加载且本地已存当前位置, 系统 SHALL 以该乡镇作为默认筛选条件并同步显示筛选标签。
2. WHILE 市场页处于打开状态, WHEN 用户从首页切换了当前位置, 系统 SHALL 在市场页重新可见时应用新的当前位置并重新加载列表。
3. WHEN 用户在市场页选择某乡镇, 系统 SHALL 更新筛选并写入本地当前位置。
4. WHEN 用户在市场页选择「全部乡镇」, 系统 SHALL 清空乡镇筛选并清除本地当前位置。
5. WHILE 用户选中某村庄, 系统 SHALL 按该村庄（含乡镇）筛选列表。

### Requirement 4 发布表单默认带出当前位置

**User Story:** AS 用户, I want 发布供应/采购时自动填上当前乡镇, so that 减少重复选择。

#### Acceptance Criteria

1. WHERE 本地已存当前位置, WHEN 用户进入新建供应发布页, 系统 SHALL 预填产地乡镇与村为当前位置。
2. WHERE 本地已存当前位置, WHEN 用户进入新建采购发布页, 系统 SHALL 预填交货地乡镇与村为当前位置。
3. WHILE 处于编辑既有发布, 系统 SHALL 保留原有产地/交货地，不覆盖为当前位置。
4. WHEN 用户在发布页主动改选区域, 系统 SHALL 以用户改选结果为准。
