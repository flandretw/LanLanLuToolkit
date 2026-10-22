# 蘭蘭露工具箱 - 代理人技能架構與部署指南

本文件為蘭蘭露工具箱的多代理人技能架構、職責定義與實戰作業流程指南。專案技能已統一配置於 `.agents/skills/`，可同時支援 **Antigravity** 與 **Codex**。

---

## 一、 架構與階段對照表

專案定義了 11 個專屬開發、優化、瘦身與審查技能：

| 階段 | 技能名稱 | 職責定義 | Antigravity 呼叫 | Codex 呼叫 |
| :--- | :--- | :--- | :--- | :--- |
| **階段 1：企劃** | `feature-planner` | 拆解需求、定義 View/Service/Strings 介面與 Git 分支建議 | `/feature-planner` | `$feature-planner` |
| **階段 2：開發** | `winui-expert` | 設計 WinUI 3 XAML 介面、4px 網格間距、Mica 材質與 Fluent Design | `/winui-expert` | `$winui-expert` |
| **階段 2：開發** | `logic-engineer` | 撰寫 Services/ 服務層 C# 邏輯、Win32 API/PInvoke 與非同步處理 | `/logic-engineer` | `$logic-engineer` |
| **階段 2：開發** | `ux-copywriter` | 依 Microsoft 風格撰寫文案，並同步產出 `zh-TW` 與 `en-US` 鍵值 | `/ux-copywriter` | `$ux-copywriter` |
| **階段 3：自檢** | `pre-flight-check` | 整合 XAML、C# 服務與資源檔，進行跨模組編譯與穩定性自檢 | `/pre-flight-check` | `$pre-flight-check` |
| **階段 4：審查與體檢** | `perf-optimizer` | 排查記憶體洩漏、GC 壓力、WinUI 3 視覺樹生命週期、高頻監控效能與非受控資源管理（限 1~3 行建議） | `/perf-optimizer` | `$perf-optimizer` |
| **階段 4：審查與體檢** | `size-slimmer` | 專注發布體積瘦身、Windows App SDK 肥大組件精簡、打包腳本優化與資產壓縮（限 1~3 行建議） | `/size-slimmer` | `$size-slimmer` |
| **階段 4：審查與體檢** | `code-quality-reviewer` | 檢查寫死常數、未使用的程式碼與潛在資源洩漏，提供精簡改進建議（限 1~3 行建議） | `/code-quality-reviewer` | `$code-quality-reviewer` |
| **階段 4：審查與體檢** | `tone-reviewer` | 審查介面文字一致性、微軟品牌語氣與雙語系對照品質（限 1~2 行建議） | `/tone-reviewer` | `$tone-reviewer` |
| **階段 4：審查與體檢** | `winui-inspector` | 審查 XAML 佈局、間距與控制項階層是否符合 Fluent Design 規範（限 1~3 行建議） | `/winui-inspector` | `$winui-inspector` |
| **階段 5：發布** | `git-specialist` | 依 Conventional Commits 撰寫聚焦「與前一版推送淨差異」之英文 Commit 訊息，非經授權絕不主動 push | `/git-specialist` | `$git-specialist` |

---

## 二、 專案目錄結構

```text
lanlanlu-toolkit/ (專案根目錄)
├── .agents/
│   └── skills/
│       ├── feature-planner/
│       │   ├── SKILL.md
│       │   └── agents/openai.yaml
│       ├── winui-expert/
│       │   ├── SKILL.md
│       │   └── agents/openai.yaml
│       ├── logic-engineer/
│       │   ├── SKILL.md
│       │   └── agents/openai.yaml
│       ├── ux-copywriter/
│       │   ├── SKILL.md
│       │   └── agents/openai.yaml
│       ├── pre-flight-check/
│       │   ├── SKILL.md
│       │   └── agents/openai.yaml
│       ├── perf-optimizer/
│       │   ├── SKILL.md
│       │   └── agents/openai.yaml
│       ├── size-slimmer/
│       │   ├── SKILL.md
│       │   └── agents/openai.yaml
│       ├── code-quality-reviewer/
│       │   ├── SKILL.md
│       │   └── agents/openai.yaml
│       ├── tone-reviewer/
│       │   ├── SKILL.md
│       │   └── agents/openai.yaml
│       ├── winui-inspector/
│       │   ├── SKILL.md
│       │   └── agents/openai.yaml
│       └── git-specialist/
│           ├── SKILL.md
│           └── agents/openai.yaml
├── docs/
│   ├── AGENTS_AND_SKILLS_GUIDE.md
│   └── SERVICES.md
├── lanlanlu-toolkit/ (主應用程式專案)
│   ├── Assets/
│   ├── Services/       (服務層，詳參 docs/SERVICES.md)
│   ├── Strings/
│   │   ├── en-US/Resources.resw
│   │   └── zh-TW/Resources.resw
│   ├── Views/         (XAML 介面與 Code-Behind)
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   └── lanlanlu-toolkit.csproj
└── scripts/
    └── Build-Portable.ps1
```

### Codex 權杖額度保護機制
在每個技能的 `agents/openai.yaml` 均包含：
```yaml
policy:
  allow_implicit_invocation: false
```
這能防止 Codex 在背景未經許可隨意載入技能消耗額度，僅在使用者輸入 `$技能名稱` 或明確調用時觸發。Antigravity 亦能無縫識別各技能之 `SKILL.md`。

---

## 三、 標準作業流程

開發新功能時的標準 5 步循環指引：

1. **【開分支與企劃】** 在 Antigravity 輸入：
   > `/feature-planner 我現在要做一個「自動清理暫存檔」的功能，請幫我規劃步驟與分支。`
2. **【寫介面與邏輯】** 建立 Git 分支後，依序呼叫：
   > `/winui-expert 請幫我設計這個功能的 XAML 頁面。`  
   > `/logic-engineer 請幫我撰寫封裝於 Services/ 的清理邏輯與系統 API 呼叫。`  
   > `/ux-copywriter 請幫我撰寫繁中與英文提示訊息，並給予 Resources.resw 鍵值。`
3. **【組裝與第一輪自檢】** 在 Antigravity 輸入：
   > `/pre-flight-check 請幫我把上面的 XAML、C# 服務與資源字串整合起來，並進行第一輪自檢除錯。`
4. **【最終審查、效能體檢與體積瘦身】** 在 Codex 或 Antigravity 中執行：
   > `$size-slimmer 檢查當前打包產出是否有沒用到的 WinAppSDK 組件、R2R 膨脹或可清理的語言資源。`  
   > `$perf-optimizer 幫我檢查此服務在高頻輪詢時是否有記憶體洩漏與過度配置物件的問題。`  
   > `$code-quality-reviewer`、`$tone-reviewer` 或 `$winui-inspector` 進行品質與規範掃描。
5. **【提交與發布】** 完成所有修改並通過審查後，呼叫：
   > `/git-specialist 我剛剛完成了暫存清理功能與多語系對照，請幫我寫 Conventional Commit 訊息與提交指令。`  
   > *（注意：該技能僅會完成本地暫存與提交，嚴格禁止未經指示主動 `git push`；內容只專注於與上一版的實質淨差異，不記錄開發過程流水帳。）*
6. **【遠端推送】** 使用者審視本地 commit 無誤後，明確指示：
   > `「請幫我推送」或「請 push 到遠端」`（代理人才會執行遠端 push 操作）。
