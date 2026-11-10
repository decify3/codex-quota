# 上游来源

本目录基于 https://github.com/bluegunsmoke/codex-quota-guardian
commit ca2fc563579ca2e38a3a9075824d03d1f1472877（MIT）。

保留原 MIT LICENSE 和源文件命名空间，更新 THIRD-PARTY-NOTICES.md 以反映实际依赖。新增改动：独立数据目录/实例/自启动标识，中英电量球、真实套餐与窗口读取、账号历史分区、逐日计划、有限历史情景预测、计划风险颜色及通知。原上游 README 描述上游能力，不等于本分支已验收。

其他两个项目仅参考设计，未复制代码：VictorZakharov/codex-usage（75cf1eaad84c5794fdf733157bd443f8d1103800）、itvincent-git/codex-usage-desktop（7a0383715367441d7ee6ba127e3b3ac097aafd2f）。参考范围包括托盘/历史展示、预测解释和跨平台组件组织。

当前为 Windows x64 公开测试版，自带运行依赖。包内 EXE 沿用 CodexGuardian.App.exe 的程序集名称，产品元数据标识为 Codex Quota；并非 OpenAI 官方软件或上游正式发行版。
