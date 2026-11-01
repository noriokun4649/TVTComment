namespace TVTComment.ViewModels
{
    enum MainWindowTab
    {
        Channel, ChatLog, ChatSource, Settings,
    }

    class ViewSettings
    {
        public bool WindowTopmost { get; set; } = false;
        public bool MinimizeOnTVTestPanelOpen { get; set; } = false;
        public double WindowOpacity { get; set; } = 1.0;
        public Model.Serialization.WindowPositionEntity MainWindowPosition { get; set; } = new Model.Serialization.WindowPositionEntity();
        public Model.Serialization.WindowPositionEntity NgSettingWindowPosition { get; set; } = new Model.Serialization.WindowPositionEntity();
        public ListViewColumnViewModel[] ChatListViewColumns { get; set; } = null;
        public double WindowFontSize { get; set; } = 12.0;
        public MainWindowTab MainWindowTab { get; set; } = MainWindowTab.Channel;
    }
}
