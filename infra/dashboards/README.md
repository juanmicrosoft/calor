# Infrastructure — Dashboards

This directory contains ARM templates for Azure dashboards and workbooks.

## Calor CLI Telemetry Workbook

**File:** `calor-cli-workbook.json`

An Application Insights Workbook that visualizes telemetry from the Calor CLI.

### Deploy

```bash
az deployment group create \
  -g Calor_AI_Language \
  --template-file infra/dashboards/calor-cli-workbook.json
```

### Sections

| Section | Tiles |
|---------|-------|
| **Key Metrics** | Total Commands, Success Rate, Unique Sessions, Avg Compile Time, Compiler Errors, Exceptions |
| **📊 Usage** | Command Usage Over Time, Command Distribution, Success vs Failure |
| **⚡ Performance** | Compilation Phase Performance (bar + table), Average Command Duration |
| **🐛 Diagnostics** | Top 20 Compiler Diagnostics, Exception Trends, Recent Exceptions |
| **🖥️ Environment** | OS, Architecture, Calor Version, Coding Agent Distribution |
| **🎛️ Features** | Compiler Feature Adoption (% using each flag) |
| **🛡️ Hook Compliance** | Compliance Summary, Decisions Over Time, Per-Agent Compliance, Recent Blocks |
| **🚨 Failures** | Last 25 Failed Commands |

### Access

Azure Portal → CalorCli (App Insights) → Workbooks → "Calor CLI - Telemetry Dashboard"
