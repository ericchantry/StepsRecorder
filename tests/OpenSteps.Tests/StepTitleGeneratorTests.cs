using OpenSteps.Core.Models;
using OpenSteps.Core.Services;

namespace OpenSteps.Tests;

public sealed class StepTitleGeneratorTests
{
    private readonly StepTitleGenerator _generator = new();

    [Fact]
    public void Generate_UsesButtonElementName()
    {
        var step = new RecordedStep { ElementName = "Save", ControlType = "Button", UsefulElementFound = true };

        Assert.Equal("Click \"Save\"", _generator.Generate(step));
    }

    [Fact]
    public void Generate_UsesMenuItemVerb()
    {
        var step = new RecordedStep { ElementName = "File", ControlType = "MenuItem", UsefulElementFound = true };

        Assert.Equal("Open the \"File\" menu", _generator.Generate(step));
    }

    [Fact]
    public void Generate_UsesEditFieldText()
    {
        var step = new RecordedStep { ElementName = "Search", ControlType = "Edit", UsefulElementFound = true };

        Assert.Equal("Click in \"Search\"", _generator.Generate(step));
    }

    [Fact]
    public void Generate_FallsBackToWindowTitle()
    {
        var step = new RecordedStep { WindowTitle = "Settings" };

        Assert.Equal("Click in Settings", _generator.Generate(step));
    }

    [Fact]
    public void Generate_FallsBackToCoordinates()
    {
        var step = new RecordedStep { ClickX = 10, ClickY = 20 };

        Assert.Equal("Click on the screen", _generator.Generate(step));
    }

    [Fact]
    public void Generate_FallsBackToWindowForGenericContainer()
    {
        var step = new RecordedStep
        {
            WindowTitle = "config.toml - Notepad",
            ControlType = "Pane",
            ClassName = "Microsoft.UI.Content.DesktopChildSiteBridge",
            UsefulElementFound = false
        };

        var result = _generator.GenerateWithReason(step);

        Assert.Equal("Click in Notepad", result.Title);
        Assert.Equal("app name fallback", result.Reason);
    }

    [Fact]
    public void Generate_FallsBackToProcessNameWhenWindowMissing()
    {
        var step = new RecordedStep { ProcessName = "Notepad", ClickX = 10, ClickY = 20 };

        Assert.Equal("Click in Notepad", _generator.Generate(step));
    }

    [Fact]
    public void Generate_CreatesTextEntryTitleForNamedTarget()
    {
        var step = new RecordedStep
        {
            ActionType = StepActionType.TextEntry,
            InputTargetName = "Search",
            KeyboardInputDetected = true,
            KeyCount = 12
        };

        Assert.Equal("Type in \"Search\"", _generator.Generate(step));
    }

    [Fact]
    public void Generate_CreatesTextEntryWindowFallback()
    {
        var step = new RecordedStep
        {
            ActionType = StepActionType.TextEntry,
            WindowTitle = "config.toml - Notepad",
            KeyboardInputDetected = true,
            KeyCount = 30
        };

        Assert.Equal("Type in Notepad", _generator.Generate(step));
    }

    [Fact]
    public void Generate_CreatesShortcutTitle()
    {
        var step = new RecordedStep
        {
            ActionType = StepActionType.Shortcut,
            ShortcutName = "Ctrl+S",
            KeyboardInputDetected = true,
            KeyCount = 1
        };

        Assert.Equal("Press Ctrl+S to save", _generator.Generate(step));
    }

    [Fact]
    public void Generate_CreatesSpecialKeyTitle()
    {
        var step = new RecordedStep
        {
            ActionType = StepActionType.SpecialKey,
            SpecialKeyName = "Enter",
            KeyboardInputDetected = true,
            KeyCount = 1
        };

        Assert.Equal("Press Enter", _generator.Generate(step));
    }

    [Fact]
    public void Generate_UsesSensitiveTextTitle()
    {
        var step = new RecordedStep
        {
            ActionType = StepActionType.TextEntry,
            InputTargetName = "Password",
            IsSensitiveInput = true,
            KeyboardInputDetected = true
        };

        var result = _generator.GenerateWithReason(step);

        Assert.Equal("Enter the password", result.Title);
        Assert.Equal("sensitive text entry", result.Reason);
    }

    [Fact]
    public void Generate_CreatesRightClickButtonTitle()
    {
        var step = new RecordedStep
        {
            ActionType = StepActionType.RightClick,
            ElementName = "Save",
            ControlType = "Button",
            UsefulElementFound = true
        };

        Assert.Equal("Right-click \"Save\"", _generator.Generate(step));
    }

    [Fact]
    public void Generate_CreatesDoubleClickItemTitle()
    {
        var step = new RecordedStep
        {
            ActionType = StepActionType.DoubleClick,
            ElementName = "Report.docx",
            ControlType = "ListItem",
            UsefulElementFound = true
        };

        Assert.Equal("Double-click \"Report.docx\"", _generator.Generate(step));
    }

    [Fact]
    public void Generate_CreatesRightClickWindowFallback()
    {
        var step = new RecordedStep { ActionType = StepActionType.RightClick, WindowTitle = "File Explorer" };

        Assert.Equal("Right-click in File Explorer", _generator.Generate(step));
    }

    [Fact]
    public void Generate_CreatesDoubleClickWindowFallback()
    {
        var step = new RecordedStep { ActionType = StepActionType.DoubleClick, WindowTitle = "File Explorer" };

        Assert.Equal("Double-click in File Explorer", _generator.Generate(step));
    }

    [Fact]
    public void Generate_KeepsApostropheInsideDoubleQuotes()
    {
        var step = new RecordedStep
        {
            ElementName = "Owner's Manual",
            ControlType = "Button",
            UsefulElementFound = true
        };

        Assert.Equal("Click \"Owner's Manual\"", _generator.Generate(step));
    }

    [Fact]
    public void Generate_UsesSingleQuotesWhenNameContainsDoubleQuote()
    {
        var step = new RecordedStep { ElementName = "Size \"Large\"", ControlType = "Button", UsefulElementFound = true };

        Assert.Equal("Click 'Size \"Large\"'", _generator.Generate(step));
    }

    [Theory]
    [InlineData("TabItem", "Home", "Open the \"Home\" tab")]
    [InlineData("ControlType.TabItem", "Home", "Open the \"Home\" tab")]
    [InlineData("ListItem", "Documents", "Select \"Documents\"")]
    [InlineData("TreeItem", "Downloads", "Select \"Downloads\"")]
    [InlineData("RadioButton", "Monthly", "Select \"Monthly\"")]
    [InlineData("CheckBox", "Remember me", "Click the \"Remember me\" checkbox")]
    [InlineData("ComboBox", "Country", "Open the \"Country\" list")]
    [InlineData("Hyperlink", "Forgot password?", "Click \"Forgot password?\"")]
    [InlineData("Document", "Body", "Click in \"Body\"")]
    public void Generate_UsesVerbThatFitsTheControl(string controlType, string name, string expected)
    {
        var step = new RecordedStep { ElementName = name, ControlType = controlType, UsefulElementFound = true };

        Assert.Equal(expected, _generator.Generate(step));
    }

    [Fact]
    public void Generate_SubmenuItemIsChosen()
    {
        var step = new RecordedStep { ElementName = "Save As...", ControlType = "MenuItem", ParentElementName = "File", UsefulElementFound = true };

        Assert.Equal("Choose \"Save As\"", _generator.Generate(step));
    }

    [Fact]
    public void Generate_RightClickOnListItemKeepsTheVerb()
    {
        var step = new RecordedStep { ActionType = StepActionType.RightClick, ElementName = "Report.docx", ControlType = "ListItem", UsefulElementFound = true };

        Assert.Equal("Right-click \"Report.docx\"", _generator.Generate(step));
    }

    [Theory]
    [InlineData("Save (Ctrl+S)", "Save")]
    [InlineData("New Tab  Ctrl+T", "New Tab")]
    [InlineData("Print...", "Print")]
    [InlineData("Options…", "Options")]
    [InlineData("User name:", "User name")]
    [InlineData("  Line one\r\n  line two ", "Line one line two")]
    [InlineData("   ", null)]
    public void CleanLabel_RemovesNoise(string input, string? expected)
    {
        Assert.Equal(expected, StepTitleGenerator.CleanLabel(input));
    }

    [Fact]
    public void CleanLabel_ShortensLongLabelsAtAWord()
    {
        var label = StepTitleGenerator.CleanLabel("Quarterly revenue summary for all regions including forecasts");

        Assert.Equal("Quarterly revenue summary for all…", label);
        Assert.True(label!.Length <= StepTitleGenerator.MaxLabelLength + 1);
    }

    [Fact]
    public void Generate_IgnoresGenericElementNames()
    {
        var step = new RecordedStep { ElementName = "Pane", ControlType = "Pane", UsefulElementFound = true, ProcessName = "EXCEL", WindowTitle = "Q3 Budget.xlsx - Excel" };

        Assert.Equal("Click in Excel", _generator.Generate(step));
    }

    [Theory]
    [InlineData("EXCEL", "Q3 Budget.xlsx - Excel", "Excel")]
    [InlineData("msedge", "Inbox - Outlook - Microsoft Edge", "Edge")]
    [InlineData("explorer", "Downloads", "File Explorer")]
    [InlineData("MyApp.exe", "Customer 1042 - Acme CRM", "Acme CRM")]
    [InlineData("MyApp", "Acme CRM", "Acme CRM")]
    [InlineData("MyApp", null, "MyApp")]
    [InlineData(null, null, null)]
    public void GetAppName_PrefersEverydayAppName(string? process, string? title, string? expected)
    {
        Assert.Equal(expected, StepTitleGenerator.GetAppName(process, title));
    }

    [Fact]
    public void Generate_TypedTextUsesShortQuotedTextAndTarget()
    {
        var step = new RecordedStep
        {
            ActionType = StepActionType.TextEntry,
            InputTargetName = "Search",
            TypedText = "invoice 1042"
        };

        Assert.Equal("Type \"invoice 1042\" in \"Search\"", _generator.Generate(step));
    }

    [Fact]
    public void Generate_TypedTextWithoutTargetUsesAppName()
    {
        var step = new RecordedStep
        {
            ActionType = StepActionType.TextEntry,
            ProcessName = "notepad",
            WindowTitle = "notes.txt - Notepad",
            TypedText = "hello\r\nworld"
        };

        Assert.Equal("Type \"hello world\" in Notepad", _generator.Generate(step));
    }

    [Fact]
    public void Generate_SensitiveFieldWithOtherNameSaysHidden()
    {
        var step = new RecordedStep { ActionType = StepActionType.TextEntry, InputTargetName = "PIN", IsSensitiveInput = true };

        Assert.Equal("Enter hidden text in \"PIN\"", _generator.Generate(step));
    }

    [Theory]
    [InlineData("Ctrl+C", "Press Ctrl+C to copy")]
    [InlineData("Shift+Win+S", "Press Shift+Win+S to take a screenshot")]
    [InlineData("Alt+F4", "Press Alt+F4 to close the window")]
    [InlineData("Ctrl+Shift+Escape", "Press Ctrl+Shift+Escape to open Task Manager")]
    [InlineData("Ctrl+Shift+P", "Press Ctrl+Shift+P")]
    public void Generate_ShortcutsSayWhatTheyDoWhenUniversal(string shortcut, string expected)
    {
        var step = new RecordedStep { ActionType = StepActionType.Shortcut, ShortcutName = shortcut };

        Assert.Equal(expected, _generator.Generate(step));
    }
}
