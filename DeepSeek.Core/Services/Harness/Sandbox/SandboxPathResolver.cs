namespace DeepSeekBrowser.Services.Harness.Sandbox;

/// <summary>虚拟路径解析 + 工作区 guard 的统一入口。</summary>
public sealed class SandboxPathResolver
{
    private readonly HarnessVirtualPathMapper _mapper;

    public SandboxPathResolver(string workspaceRoot) =>
        _mapper = new HarnessVirtualPathMapper(workspaceRoot);

    public HarnessVirtualPathMapper Mapper => _mapper;

    public string ResolveRead(string? path) => _mapper.ResolveToPhysical(path);

    public string ResolveWrite(string? path)
    {
        if (_mapper.IsReadOnlyTarget(path))
            throw new UnauthorizedAccessException("该虚拟路径为只读: " + path);
        return _mapper.ResolveToPhysical(path);
    }

    public string ToVirtual(string physicalPath) => _mapper.ResolveToVirtual(physicalPath);

    public string VirtualizeText(string text) => _mapper.VirtualizeText(text);
}
