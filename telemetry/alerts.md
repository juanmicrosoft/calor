# Calor CLI — Alert Definitions

Alert rules for Azure Monitor / Application Insights to detect anomalies in Calor CLI telemetry.

---

## 1. Exception Rate Spike

**Condition**: Exception count exceeds 3x the baseline average in a 1-hour window.

**KQL**:
```kql
let baseline = toscalar(
    exceptions
    | where timestamp between (ago(7d) .. ago(1h))
    | summarize count() / 168.0  // avg per hour over 7d
);
exceptions
| where timestamp >= ago(1h)
| summarize CurrentCount = count()
| where CurrentCount > baseline * 3
```

**Threshold**: `CurrentCount > baseline * 3`
**Window**: 1 hour
**Frequency**: Every 15 minutes
**Severity**: Sev 2 (High)
**Action**: Notify engineering channel, create incident if sustained for 2 consecutive windows.

---

## 2. Conversion Failure Rate

**Condition**: Conversion failure rate exceeds 25% over 4 hours with at least 10 attempts.

**KQL**:
```kql
customEvents
| where name == 'ConversionAttempted'
| where timestamp >= ago(4h)
| extend success = tostring(customDimensions.success) == 'True'
| summarize Total = count(), Failed = countif(not(success))
| where Total >= 10
| extend FailRate = round(100.0 * Failed / Total, 1)
| where FailRate > 25
```

**Threshold**: `FailRate > 25%` with `Total >= 10`
**Window**: 4 hours
**Frequency**: Every 30 minutes
**Severity**: Sev 2 (High)
**Action**: Notify engineering channel, investigate latest converter changes.

---

## 3. New Diagnostic Code Pattern

**Condition**: A diagnostic code not seen in the previous 30 days appears more than 5 times in 7 days.

**KQL**:
```kql
let known_codes = customEvents
| where name == 'DiagnosticOccurrence'
| where timestamp between (ago(37d) .. ago(7d))
| distinct tostring(customDimensions.code);
customEvents
| where name == 'DiagnosticOccurrence'
| where timestamp >= ago(7d)
| extend code = tostring(customDimensions.code)
| where code !in (known_codes)
| summarize Count = count() by code
| where Count > 5
```

**Threshold**: `Count > 5` for any previously-unseen code
**Window**: 7 days
**Frequency**: Daily
**Severity**: Sev 3 (Moderate)
**Action**: Review new diagnostic code, determine if it indicates a regression or new language feature.

---

## 4. Version Regression Detected

**Condition**: Same `inputHash` produces success on one version but failure on another.

**KQL**:
```kql
customEvents
| where name == 'CompilationOutcome'
| where timestamp >= ago(7d)
| extend inputHash = tostring(customDimensions.inputHash),
    success = tostring(customDimensions.success),
    version = tostring(customDimensions.version)
| summarize Outcomes = make_set(success), Versions = make_set(version) by inputHash
| where array_length(Outcomes) > 1
| where Outcomes has 'True' and Outcomes has 'False'
```

**Threshold**: Any row returned (regression detected)
**Window**: 7 days
**Frequency**: Every 6 hours
**Severity**: Sev 1 (Critical)
**Action**: Immediately investigate. Compare versions in `Versions` set. Bisect compiler changes between the two versions.

---

## 5. Hook Compliance Drop

**Condition**: Hook compliance rate drops below 80% in a 24-hour window.

**KQL**:
```kql
customEvents
| where name in ('HookAllow', 'HookBlock')
| where timestamp >= ago(24h)
| summarize
    Allows = countif(name == 'HookAllow'),
    Total = count()
| extend ComplianceRate = iff(Total > 0, round(100.0 * Allows / Total, 1), 100.0)
| where ComplianceRate < 80
```

**Threshold**: `ComplianceRate < 80%`
**Window**: 24 hours
**Frequency**: Every 1 hour
**Severity**: Sev 3 (Moderate)
**Action**: Review blocked hook events to identify misconfigured agents or unexpected file writes.

---

## 6. Session Duration Anomaly

**Condition**: Session duration exceeds 10x the P95 over the past 7 days.

**KQL**:
```kql
let p95 = toscalar(
    customEvents
    | where name == 'SessionEnded'
    | where timestamp between (ago(7d) .. ago(1h))
    | extend sessionMs = todouble(customMeasurements.sessionDurationMs)
    | summarize percentile(sessionMs, 95)
);
customEvents
| where name == 'SessionEnded'
| where timestamp >= ago(1h)
| extend sessionMs = todouble(customMeasurements.sessionDurationMs)
| where sessionMs > p95 * 10
| project timestamp, sessionMs, tostring(customDimensions.commandSequence)
```

**Threshold**: `sessionMs > P95 * 10`
**Window**: 1 hour (compared to 7-day baseline)
**Frequency**: Every 30 minutes
**Severity**: Sev 3 (Moderate)
**Action**: Investigate unusually long sessions — may indicate infinite loops, solver timeouts, or stuck commands.
