using DeepSeekBrowser.Services;

var configDir = args.Length > 0
    ? args[0]
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "deepseek_desktop");

var outcome = ConfigFileRepair.TryRepairUserConfigFile(configDir);
Console.WriteLine(outcome.ToString());

Environment.Exit(outcome switch
{
    ConfigFileRepair.RepairOutcome.StillInvalid => 2,
    ConfigFileRepair.RepairOutcome.NoFile => 0,
    _ => 0
});
