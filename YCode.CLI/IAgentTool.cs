namespace YCode.CLI
{
    internal interface IAgentTool
    {
        string Name { get; }
        string Description { get; }
        bool IsReadOnly { get; }
        bool IsEnable { get; }

        Delegate Handler { get; }
    }
}


