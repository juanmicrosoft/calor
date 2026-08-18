using Calor.Compiler.Ast;

namespace Calor.Compiler.CodeGen;

public sealed record CrossModuleFunctionTarget(
    string ModuleName,
    string NamespaceIdentity,
    string ModuleClassName)
{
    internal static CrossModuleFunctionTarget? Create(
        ModuleNode module,
        FunctionNode function)
    {
        var namespaceIdentity = function.NamespaceIdentity;
        if (namespaceIdentity == null)
        {
            var hasExplicitTopology =
                module.NamespaceScopes.Count > 0
                || module.Usings.Any(item => item.NamespaceScopeId != null)
                || module.Interfaces.Cast<AstNode>()
                    .Concat(module.Enums)
                    .Concat(module.EnumExtensions)
                    .Concat(module.Delegates)
                    .Concat(module.Classes)
                    .Concat(module.Functions)
                    .Concat(module.InteropBlocks)
                    .Concat(module.TypePreprocessorBlocks)
                    .Any(item =>
                        item.NamespaceScopeId != null
                        && !(item.NamespaceScopeId == ""
                             && !string.IsNullOrEmpty(
                                 item.NamespaceIdentity)));
            if (hasExplicitTopology)
                return null;

            namespaceIdentity =
                string.IsNullOrEmpty(module.Name) || module.Name == "_global"
                    ? ""
                    : module.Name;
        }

        var moduleClassName = string.IsNullOrEmpty(namespaceIdentity)
            ? "GlobalModule"
            : namespaceIdentity.Split('.').Last() + "Module";
        return new CrossModuleFunctionTarget(
            module.Name,
            namespaceIdentity,
            moduleClassName);
    }
}
