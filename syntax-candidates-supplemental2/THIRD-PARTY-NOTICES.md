# Third-party notices for derived payloads

Some payloads in this directory are derived from third-party C# source. The Calor
repository license does not cover them; each derived payload remains under its
upstream license, reproduced byte-for-byte from the pinned commit in `third-party-licenses/`
(git blob IDs listed below match upstream).

**Modification notice (Apache-2.0 §4(b)).** Every derived payload listed here is a
modified work: the upstream C# file was machine-converted to Calor by the Calor
converter and, for non-`current` variants, rewritten into candidate syntaxes for
token counting. These payloads are measurement inputs, not upstream source.

Payloads not listed here (agent files, primer, init/MCP context and hand-authored probes)
are Calor project material. This file and `third-party-licenses/` are not measured
and are not part of `inputs_sha256`.

## Upstream corpora

| Corpus | Upstream | Pinned commit | License | Copy here | Upstream blob |
|---|---|---|---|---|---|
| MediatR | https://github.com/jbogard/MediatR | `fb309026775ef953a64fb5339d074426c1ad2c37` | Apache-2.0 (`LICENSE`) | `third-party-licenses/MediatR-LICENSE` | `e06d2081865a766a8668acc12878f98b27fc9ea0` |
| Serilog | https://github.com/serilog/serilog | `0597ddfbd4ec594d9c42edd745fe728a2198bad9` | Apache-2.0 (`LICENSE`) | `third-party-licenses/serilog-LICENSE` | `37ec93a14fdcd0d6e525d97c0cfa6b314eaa98d8` |
| FluentValidation | https://github.com/FluentValidation/FluentValidation | `71b3c60cb5a16e02cb7957e478ec3fb6b983a73c` | Apache-2.0 (`License.txt`) | `third-party-licenses/FluentValidation-License.txt` | `7cd40e552f41493c9a4ac5934ac471285e7ae871` |

## Derived payload mapping

### MediatR: `src/MediatR/IMediator.cs`

Upstream file has no per-file notice; covered by the MediatR repository license above.

Payloads:
- `current/converted-MediatR-IMediator.txt` (converted-MediatR-IMediator)
- `infix/converted-MediatR-IMediator.txt` (converted-MediatR-IMediator)
- `line-call/converted-MediatR-IMediator.txt` (converted-MediatR-IMediator)
- `sexpr/converted-MediatR-IMediator.txt` (converted-MediatR-IMediator)
- `active-abcaa63af/probes/active-converted-MediatR-IMediator.txt` (probe-active-converted-MediatR-IMediator)
- `current/converted-MediatR-IMediator.txt` (probe-active-converted-MediatR-IMediator)

### MediatR: `src/MediatR/INotificationPublisher.cs`

Upstream file has no per-file notice; covered by the MediatR repository license above.

Payloads:
- `current/converted-MediatR-INotificationPublisher.txt` (converted-MediatR-INotificationPublisher)
- `infix/converted-MediatR-INotificationPublisher.txt` (converted-MediatR-INotificationPublisher)
- `line-call/converted-MediatR-INotificationPublisher.txt` (converted-MediatR-INotificationPublisher)
- `sexpr/converted-MediatR-INotificationPublisher.txt` (converted-MediatR-INotificationPublisher)
- `active-abcaa63af/probes/active-converted-MediatR-INotificationPublisher.txt` (probe-active-converted-MediatR-INotificationPublisher)
- `current/converted-MediatR-INotificationPublisher.txt` (probe-active-converted-MediatR-INotificationPublisher)

### MediatR: `src/MediatR/MicrosoftExtensionsDI/RequestExceptionActionProcessorStrategy.cs`

Upstream file has no per-file notice; covered by the MediatR repository license above.

Payloads:
- `current/converted-MediatR-RequestExceptionActionProcessorStrategy.txt` (converted-MediatR-RequestExceptionActionProcessorStrategy)
- `infix/converted-MediatR-RequestExceptionActionProcessorStrategy.txt` (converted-MediatR-RequestExceptionActionProcessorStrategy)
- `line-call/converted-MediatR-RequestExceptionActionProcessorStrategy.txt` (converted-MediatR-RequestExceptionActionProcessorStrategy)
- `sexpr/converted-MediatR-RequestExceptionActionProcessorStrategy.txt` (converted-MediatR-RequestExceptionActionProcessorStrategy)
- `active-abcaa63af/probes/active-converted-MediatR-RequestExceptionActionProcessorStrategy.txt` (probe-active-converted-MediatR-RequestExceptionActionProcessorStrategy)
- `current/converted-MediatR-RequestExceptionActionProcessorStrategy.txt` (probe-active-converted-MediatR-RequestExceptionActionProcessorStrategy)

### MediatR: `src/MediatR/NotificationPublishers/ForeachAwaitPublisher.cs`

Upstream file has no per-file notice; covered by the MediatR repository license above.

Payloads:
- `current/converted-MediatR-ForeachAwaitPublisher.txt` (converted-MediatR-ForeachAwaitPublisher)
- `infix/converted-MediatR-ForeachAwaitPublisher.txt` (converted-MediatR-ForeachAwaitPublisher)
- `line-call/converted-MediatR-ForeachAwaitPublisher.txt` (converted-MediatR-ForeachAwaitPublisher)
- `sexpr/converted-MediatR-ForeachAwaitPublisher.txt` (converted-MediatR-ForeachAwaitPublisher)
- `active-abcaa63af/probes/active-converted-MediatR-ForeachAwaitPublisher.txt` (probe-active-converted-MediatR-ForeachAwaitPublisher)
- `current/converted-MediatR-ForeachAwaitPublisher.txt` (probe-active-converted-MediatR-ForeachAwaitPublisher)

### MediatR: `src/MediatR/Pipeline/IRequestPostProcessor.cs`

Upstream file has no per-file notice; covered by the MediatR repository license above.

Payloads:
- `current/converted-MediatR-IRequestPostProcessor.txt` (converted-MediatR-IRequestPostProcessor)
- `infix/converted-MediatR-IRequestPostProcessor.txt` (converted-MediatR-IRequestPostProcessor)
- `line-call/converted-MediatR-IRequestPostProcessor.txt` (converted-MediatR-IRequestPostProcessor)
- `sexpr/converted-MediatR-IRequestPostProcessor.txt` (converted-MediatR-IRequestPostProcessor)
- `active-abcaa63af/probes/active-converted-MediatR-IRequestPostProcessor.txt` (probe-active-converted-MediatR-IRequestPostProcessor)
- `current/converted-MediatR-IRequestPostProcessor.txt` (probe-active-converted-MediatR-IRequestPostProcessor)

### MediatR: `src/MediatR/Pipeline/IRequestPreProcessor.cs`

Upstream file has no per-file notice; covered by the MediatR repository license above.

Payloads:
- `current/converted-MediatR-IRequestPreProcessor.txt` (converted-MediatR-IRequestPreProcessor)
- `infix/converted-MediatR-IRequestPreProcessor.txt` (converted-MediatR-IRequestPreProcessor)
- `line-call/converted-MediatR-IRequestPreProcessor.txt` (converted-MediatR-IRequestPreProcessor)
- `sexpr/converted-MediatR-IRequestPreProcessor.txt` (converted-MediatR-IRequestPreProcessor)
- `active-abcaa63af/probes/active-converted-MediatR-IRequestPreProcessor.txt` (probe-active-converted-MediatR-IRequestPreProcessor)
- `current/converted-MediatR-IRequestPreProcessor.txt` (probe-active-converted-MediatR-IRequestPreProcessor)

### Serilog: `src/Serilog/Configuration/ILoggerSettings.cs`

Upstream file notice (retained verbatim):

```
// Copyright 2013-2015 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
```

Payloads:
- `current/converted-Serilog-ILoggerSettings.txt` (converted-Serilog-ILoggerSettings)
- `infix/converted-Serilog-ILoggerSettings.txt` (converted-Serilog-ILoggerSettings)
- `line-call/converted-Serilog-ILoggerSettings.txt` (converted-Serilog-ILoggerSettings)
- `sexpr/converted-Serilog-ILoggerSettings.txt` (converted-Serilog-ILoggerSettings)
- `active-abcaa63af/probes/active-converted-Serilog-ILoggerSettings.txt` (probe-active-converted-Serilog-ILoggerSettings)
- `current/converted-Serilog-ILoggerSettings.txt` (probe-active-converted-Serilog-ILoggerSettings)

### Serilog: `src/Serilog/Core/Constants.cs`

Upstream file notice (retained verbatim):

```
// Copyright 2013-2015 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
```

Payloads:
- `current/converted-Serilog-Constants.txt` (converted-Serilog-Constants)
- `infix/converted-Serilog-Constants.txt` (converted-Serilog-Constants)
- `line-call/converted-Serilog-Constants.txt` (converted-Serilog-Constants)
- `sexpr/converted-Serilog-Constants.txt` (converted-Serilog-Constants)
- `active-abcaa63af/probes/active-converted-Serilog-Constants.txt` (probe-active-converted-Serilog-Constants)
- `current/converted-Serilog-Constants.txt` (probe-active-converted-Serilog-Constants)

### Serilog: `src/Serilog/Core/ILogEventEnricher.cs`

Upstream file notice (retained verbatim):

```
// Copyright 2013-2015 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
```

Payloads:
- `current/converted-Serilog-ILogEventEnricher.txt` (converted-Serilog-ILogEventEnricher)
- `infix/converted-Serilog-ILogEventEnricher.txt` (converted-Serilog-ILogEventEnricher)
- `line-call/converted-Serilog-ILogEventEnricher.txt` (converted-Serilog-ILogEventEnricher)
- `sexpr/converted-Serilog-ILogEventEnricher.txt` (converted-Serilog-ILogEventEnricher)
- `active-abcaa63af/probes/active-converted-Serilog-ILogEventEnricher.txt` (probe-active-converted-Serilog-ILogEventEnricher)
- `current/converted-Serilog-ILogEventEnricher.txt` (probe-active-converted-Serilog-ILogEventEnricher)

### Serilog: `src/Serilog/Core/ILogEventFilter.cs`

Upstream file notice (retained verbatim):

```
// Copyright 2013-2015 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
```

Payloads:
- `current/converted-Serilog-ILogEventFilter.txt` (converted-Serilog-ILogEventFilter)
- `infix/converted-Serilog-ILogEventFilter.txt` (converted-Serilog-ILogEventFilter)
- `line-call/converted-Serilog-ILogEventFilter.txt` (converted-Serilog-ILogEventFilter)
- `sexpr/converted-Serilog-ILogEventFilter.txt` (converted-Serilog-ILogEventFilter)
- `active-abcaa63af/probes/active-converted-Serilog-ILogEventFilter.txt` (probe-active-converted-Serilog-ILogEventFilter)
- `current/converted-Serilog-ILogEventFilter.txt` (probe-active-converted-Serilog-ILogEventFilter)

### Serilog: `src/Serilog/Core/LoggingFailureKind.cs`

Upstream file notice (retained verbatim):

```
// Copyright © Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
```

Payloads:
- `current/converted-Serilog-LoggingFailureKind.txt` (converted-Serilog-LoggingFailureKind)
- `infix/converted-Serilog-LoggingFailureKind.txt` (converted-Serilog-LoggingFailureKind)
- `line-call/converted-Serilog-LoggingFailureKind.txt` (converted-Serilog-LoggingFailureKind)
- `sexpr/converted-Serilog-LoggingFailureKind.txt` (converted-Serilog-LoggingFailureKind)
- `active-abcaa63af/probes/active-converted-Serilog-LoggingFailureKind.txt` (probe-active-converted-Serilog-LoggingFailureKind)
- `current/converted-Serilog-LoggingFailureKind.txt` (probe-active-converted-Serilog-LoggingFailureKind)

### Serilog: `src/Serilog/Parsing/AlignmentDirection.cs`

Upstream file notice (retained verbatim):

```
// Copyright 2013-2015 Serilog Contributors
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
```

Payloads:
- `current/converted-Serilog-AlignmentDirection.txt` (converted-Serilog-AlignmentDirection)
- `infix/converted-Serilog-AlignmentDirection.txt` (converted-Serilog-AlignmentDirection)
- `line-call/converted-Serilog-AlignmentDirection.txt` (converted-Serilog-AlignmentDirection)
- `sexpr/converted-Serilog-AlignmentDirection.txt` (converted-Serilog-AlignmentDirection)
- `active-abcaa63af/probes/active-converted-Serilog-AlignmentDirection.txt` (probe-active-converted-Serilog-AlignmentDirection)
- `current/converted-Serilog-AlignmentDirection.txt` (probe-active-converted-Serilog-AlignmentDirection)

### FluentValidation: `src/FluentValidation/InlineValidator.cs`

Upstream file notice (retained verbatim):

```
#region License
// Copyright (c) .NET Foundation and contributors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// The latest version of this file can be found at https://github.com/FluentValidation/FluentValidation
#endregion
```

Payloads:
- `current/converted-FluentValidation-InlineValidator.txt` (converted-FluentValidation-InlineValidator)
- `infix/converted-FluentValidation-InlineValidator.txt` (converted-FluentValidation-InlineValidator)
- `line-call/converted-FluentValidation-InlineValidator.txt` (converted-FluentValidation-InlineValidator)
- `sexpr/converted-FluentValidation-InlineValidator.txt` (converted-FluentValidation-InlineValidator)
- `active-abcaa63af/probes/active-converted-FluentValidation-InlineValidator.txt` (probe-active-converted-FluentValidation-InlineValidator)
- `current/converted-FluentValidation-InlineValidator.txt` (probe-active-converted-FluentValidation-InlineValidator)

### FluentValidation: `src/FluentValidation/Internal/ChildRulesContainer.cs`

Upstream file notice (retained verbatim):

```
#region License
// Copyright (c) .NET Foundation and contributors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// The latest version of this file can be found at https://github.com/FluentValidation/FluentValidation
#endregion
```

Payloads:
- `current/converted-FluentValidation-ChildRulesContainer.txt` (converted-FluentValidation-ChildRulesContainer)
- `infix/converted-FluentValidation-ChildRulesContainer.txt` (converted-FluentValidation-ChildRulesContainer)
- `line-call/converted-FluentValidation-ChildRulesContainer.txt` (converted-FluentValidation-ChildRulesContainer)
- `sexpr/converted-FluentValidation-ChildRulesContainer.txt` (converted-FluentValidation-ChildRulesContainer)
- `active-abcaa63af/probes/active-converted-FluentValidation-ChildRulesContainer.txt` (probe-active-converted-FluentValidation-ChildRulesContainer)
- `current/converted-FluentValidation-ChildRulesContainer.txt` (probe-active-converted-FluentValidation-ChildRulesContainer)

### FluentValidation: `src/FluentValidation/Internal/CompositeValidatorSelector.cs`

Upstream file notice (retained verbatim):

```
#region License
// Copyright (c) .NET Foundation and contributors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// The latest version of this file can be found at https://github.com/FluentValidation/FluentValidation
#endregion
```

Payloads:
- `current/converted-FluentValidation-CompositeValidatorSelector.txt` (converted-FluentValidation-CompositeValidatorSelector)
- `infix/converted-FluentValidation-CompositeValidatorSelector.txt` (converted-FluentValidation-CompositeValidatorSelector)
- `line-call/converted-FluentValidation-CompositeValidatorSelector.txt` (converted-FluentValidation-CompositeValidatorSelector)
- `sexpr/converted-FluentValidation-CompositeValidatorSelector.txt` (converted-FluentValidation-CompositeValidatorSelector)
- `active-abcaa63af/probes/active-converted-FluentValidation-CompositeValidatorSelector.txt` (probe-active-converted-FluentValidation-CompositeValidatorSelector)
- `current/converted-FluentValidation-CompositeValidatorSelector.txt` (probe-active-converted-FluentValidation-CompositeValidatorSelector)

### FluentValidation: `src/FluentValidation/Internal/IValidatorSelector.cs`

Upstream file notice (retained verbatim):

```
#region License
// Copyright (c) .NET Foundation and contributors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// The latest version of this file can be found at https://github.com/FluentValidation/FluentValidation
#endregion
```

Payloads:
- `current/converted-FluentValidation-IValidatorSelector.txt` (converted-FluentValidation-IValidatorSelector)
- `infix/converted-FluentValidation-IValidatorSelector.txt` (converted-FluentValidation-IValidatorSelector)
- `line-call/converted-FluentValidation-IValidatorSelector.txt` (converted-FluentValidation-IValidatorSelector)
- `sexpr/converted-FluentValidation-IValidatorSelector.txt` (converted-FluentValidation-IValidatorSelector)
- `active-abcaa63af/probes/active-converted-FluentValidation-IValidatorSelector.txt` (probe-active-converted-FluentValidation-IValidatorSelector)
- `current/converted-FluentValidation-IValidatorSelector.txt` (probe-active-converted-FluentValidation-IValidatorSelector)

### FluentValidation: `src/FluentValidation/Resources/ILanguageManager.cs`

Upstream file notice (retained verbatim):

```
#region License
// Copyright (c) .NET Foundation and contributors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// The latest version of this file can be found at https://github.com/FluentValidation/FluentValidation
#endregion
```

Payloads:
- `current/converted-FluentValidation-ILanguageManager.txt` (converted-FluentValidation-ILanguageManager)
- `infix/converted-FluentValidation-ILanguageManager.txt` (converted-FluentValidation-ILanguageManager)
- `line-call/converted-FluentValidation-ILanguageManager.txt` (converted-FluentValidation-ILanguageManager)
- `sexpr/converted-FluentValidation-ILanguageManager.txt` (converted-FluentValidation-ILanguageManager)
- `active-abcaa63af/probes/active-converted-FluentValidation-ILanguageManager.txt` (probe-active-converted-FluentValidation-ILanguageManager)
- `current/converted-FluentValidation-ILanguageManager.txt` (probe-active-converted-FluentValidation-ILanguageManager)

### FluentValidation: `src/FluentValidation/Validators/NoopPropertyValidator.cs`

Upstream file notice (retained verbatim):

```
#region License
// Copyright (c) .NET Foundation and contributors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// The latest version of this file can be found at https://github.com/FluentValidation/FluentValidation
#endregion
```

Payloads:
- `current/converted-FluentValidation-NoopPropertyValidator.txt` (converted-FluentValidation-NoopPropertyValidator)
- `infix/converted-FluentValidation-NoopPropertyValidator.txt` (converted-FluentValidation-NoopPropertyValidator)
- `line-call/converted-FluentValidation-NoopPropertyValidator.txt` (converted-FluentValidation-NoopPropertyValidator)
- `sexpr/converted-FluentValidation-NoopPropertyValidator.txt` (converted-FluentValidation-NoopPropertyValidator)
- `active-abcaa63af/probes/active-converted-FluentValidation-NoopPropertyValidator.txt` (probe-active-converted-FluentValidation-NoopPropertyValidator)
- `current/converted-FluentValidation-NoopPropertyValidator.txt` (probe-active-converted-FluentValidation-NoopPropertyValidator)
