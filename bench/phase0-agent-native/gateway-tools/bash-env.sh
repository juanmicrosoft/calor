# Native login-shell startup can reorder PATH ahead of the registered dotnet shim.
export PATH="${PPW_MODEL_TOOL_PATH:?missing registered model tool path}"
