namespace DesktopComputerUse.TestApp;

internal sealed class MainForm : Form
{
    private const string ReadyStatus = "Ready";
    private const string DelayedReadyStatus = "Delayed action ready";

    private readonly TextBox _customerNameTextBox = new();
    private readonly Button _saveButton = new();
    private readonly CheckBox _enabledCheckBox = new();
    private readonly ComboBox _roleComboBox = new();
    private readonly TabControl _mainTabControl = new();
    private readonly TreeView _navigationTreeView = new();
    private readonly DataGridView _customerDataGrid = new();
    private readonly ToolStripMenuItem _resetMenuItem = new();
    private readonly GroupBox _expandableDetailsGroup = new();
    private readonly Button _toggleDetailsButton = new();
    private readonly Panel _expandableContentPanel = new();
    private readonly Button _modalDialogButton = new();
    private readonly Label _validationStatusLabel = new();
    private readonly Button _disabledActionButton = new();
    private readonly Button _delayedActionButton = new();
    private readonly Label _delayedStatusLabel = new();
    private readonly System.Windows.Forms.Timer _delayedTimer = new() { Interval = 600 };

    public MainForm()
    {
        ConfigureForm();
        ConfigureControls();
        Controls.Add(CreateMainLayout());
        Controls.Add(CreateMenu());
        WireEvents();
        ResetFixture();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ActiveControl = _customerNameTextBox;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _delayedTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ConfigureForm()
    {
        Name = "MainForm";
        AccessibleName = "Desktop computer use test application";
        AccessibleDescription = "A deterministic WinForms fixture for desktop automation tests.";
        Text = "Desktop Computer Use Test App";
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(1000, 700);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
    }

    private void ConfigureControls()
    {
        _customerNameTextBox.Name = "CustomerNameTextBox";
        _customerNameTextBox.AccessibleName = "Customer name";
        _customerNameTextBox.AccessibleDescription = "Enter the customer name to validate and save.";
        _customerNameTextBox.Width = 260;

        _saveButton.Name = "SaveButton";
        _saveButton.AccessibleName = "Save customer";
        _saveButton.AccessibleDescription = "Validates and saves the current customer name.";
        _saveButton.Text = "Save";
        _saveButton.AutoSize = true;

        _enabledCheckBox.Name = "EnabledCheckBox";
        _enabledCheckBox.AccessibleName = "Customer enabled";
        _enabledCheckBox.AccessibleDescription = "Controls the deterministic enabled state of the customer.";
        _enabledCheckBox.Text = "Enabled";
        _enabledCheckBox.AutoSize = true;

        _roleComboBox.Name = "RoleComboBox";
        _roleComboBox.AccessibleName = "Customer role";
        _roleComboBox.AccessibleDescription = "Select a role from the fixed list of customer roles.";
        _roleComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _roleComboBox.Width = 220;
        _roleComboBox.Items.AddRange(["Administrator", "Editor", "Viewer"]);

        _mainTabControl.Name = "MainTabControl";
        _mainTabControl.AccessibleName = "Test content tabs";
        _mainTabControl.AccessibleDescription = "Switches between the customer grid and expandable details.";
        _mainTabControl.Dock = DockStyle.Fill;

        ConfigureTree();
        ConfigureGrid();
        ConfigureExpandableDetails();

        _modalDialogButton.Name = "ModalDialogButton";
        _modalDialogButton.AccessibleName = "Open modal dialog";
        _modalDialogButton.AccessibleDescription = "Opens a deterministic modal confirmation dialog.";
        _modalDialogButton.Text = "Open dialog";
        _modalDialogButton.AutoSize = true;

        _validationStatusLabel.Name = "ValidationStatusLabel";
        _validationStatusLabel.AccessibleName = "Validation status";
        _validationStatusLabel.AccessibleDescription = "Reports the result of customer validation and save actions.";
        _validationStatusLabel.AutoSize = true;

        _disabledActionButton.Name = "DisabledActionButton";
        _disabledActionButton.AccessibleName = "Disabled action";
        _disabledActionButton.AccessibleDescription = "A permanently disabled control for automation state tests.";
        _disabledActionButton.Text = "Disabled action";
        _disabledActionButton.Enabled = false;
        _disabledActionButton.AutoSize = true;

        _delayedActionButton.Name = "DelayedActionButton";
        _delayedActionButton.AccessibleName = "Run delayed action";
        _delayedActionButton.AccessibleDescription = "Starts a fixed delay before updating the delayed status.";
        _delayedActionButton.Text = "Run delayed action";
        _delayedActionButton.AutoSize = true;

        _delayedStatusLabel.Name = "DelayedStatusLabel";
        _delayedStatusLabel.AccessibleName = "Delayed action status";
        _delayedStatusLabel.AccessibleDescription = "Reports the deterministic delayed action state.";
        _delayedStatusLabel.AutoSize = true;
    }

    private void ConfigureTree()
    {
        _navigationTreeView.Name = "NavigationTreeView";
        _navigationTreeView.AccessibleName = "Navigation tree";
        _navigationTreeView.AccessibleDescription = "A fixed hierarchy whose nodes can be expanded and collapsed.";
        _navigationTreeView.Dock = DockStyle.Fill;
        _navigationTreeView.HideSelection = false;
        _navigationTreeView.LabelEdit = false;

        var departmentsNode = new TreeNode("Departments") { Name = "DepartmentsNode" };
        var engineeringNode = new TreeNode("Engineering") { Name = "EngineeringNode" };
        engineeringNode.Nodes.Add(new TreeNode("Platform") { Name = "PlatformNode" });
        engineeringNode.Nodes.Add(new TreeNode("Quality") { Name = "QualityNode" });
        departmentsNode.Nodes.Add(engineeringNode);
        departmentsNode.Nodes.Add(new TreeNode("Sales") { Name = "SalesNode" });

        var regionsNode = new TreeNode("Regions") { Name = "RegionsNode" };
        regionsNode.Nodes.Add(new TreeNode("North America") { Name = "NorthAmericaNode" });
        regionsNode.Nodes.Add(new TreeNode("Europe") { Name = "EuropeNode" });

        _navigationTreeView.Nodes.AddRange([departmentsNode, regionsNode]);
    }

    private void ConfigureGrid()
    {
        _customerDataGrid.Name = "CustomerDataGrid";
        _customerDataGrid.AccessibleName = "Customers data grid";
        _customerDataGrid.AccessibleDescription = "A read-only grid containing a fixed set of customer rows.";
        _customerDataGrid.Dock = DockStyle.Fill;
        _customerDataGrid.AllowUserToAddRows = false;
        _customerDataGrid.AllowUserToDeleteRows = false;
        _customerDataGrid.AllowUserToOrderColumns = false;
        _customerDataGrid.AllowUserToResizeColumns = false;
        _customerDataGrid.AllowUserToResizeRows = false;
        _customerDataGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _customerDataGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _customerDataGrid.MultiSelect = false;
        _customerDataGrid.ReadOnly = true;
        _customerDataGrid.RowHeadersVisible = false;
        _customerDataGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

        _customerDataGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CustomerIdColumn",
            HeaderText = "ID",
            FillWeight = 20
        });
        _customerDataGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CustomerNameColumn",
            HeaderText = "Name",
            FillWeight = 45
        });
        _customerDataGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "CustomerRoleColumn",
            HeaderText = "Role",
            FillWeight = 30
        });
        _customerDataGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "CustomerEnabledColumn",
            HeaderText = "Enabled",
            FillWeight = 20
        });

        _customerDataGrid.Rows.Add("1001", "Ada Lovelace", "Administrator", true);
        _customerDataGrid.Rows.Add("1002", "Grace Hopper", "Editor", false);
        _customerDataGrid.Rows.Add("1003", "Alan Turing", "Viewer", true);
    }

    private void ConfigureExpandableDetails()
    {
        _expandableDetailsGroup.Name = "ExpandableDetailsGroup";
        _expandableDetailsGroup.AccessibleName = "Expandable customer details";
        _expandableDetailsGroup.AccessibleDescription = "A group whose content can be shown or hidden.";
        _expandableDetailsGroup.Text = "Customer details";
        _expandableDetailsGroup.Dock = DockStyle.Fill;

        _toggleDetailsButton.Name = "ToggleDetailsButton";
        _toggleDetailsButton.AccessibleName = "Toggle customer details";
        _toggleDetailsButton.AccessibleDescription = "Expands or collapses the customer details content.";
        _toggleDetailsButton.AutoSize = true;

        _expandableContentPanel.Name = "ExpandableContentPanel";
        _expandableContentPanel.AccessibleName = "Customer details content";
        _expandableContentPanel.AccessibleDescription = "Content controlled by the customer details toggle.";
        _expandableContentPanel.Dock = DockStyle.Fill;
        _expandableContentPanel.Controls.Add(new Label
        {
            Name = "DetailsContentLabel",
            AccessibleName = "Customer details text",
            AccessibleDescription = "Static content inside the expandable details group.",
            Text = "Deterministic detail content",
            AutoSize = true,
            Location = new Point(8, 12)
        });

        _expandableDetailsGroup.Controls.Add(_expandableContentPanel);
        _expandableDetailsGroup.Controls.Add(_toggleDetailsButton);
        _toggleDetailsButton.Dock = DockStyle.Top;
    }

    private Control CreateMainLayout()
    {
        var layout = new TableLayoutPanel
        {
            Name = "MainLayout",
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 12, 12, 12),
            RowCount = 2,
            ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(CreateCustomerPanel(), 0, 0);
        layout.Controls.Add(CreateContentPanel(), 0, 1);
        return layout;
    }

    private Control CreateCustomerPanel()
    {
        var group = new GroupBox
        {
            Name = "CustomerFormGroup",
            AccessibleName = "Customer form",
            AccessibleDescription = "Customer inputs and deterministic test actions.",
            Text = "Customer",
            Dock = DockStyle.Fill
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            RowCount = 3,
            ColumnCount = 4
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(CreateLabel("CustomerNameLabel", "Customer name:", _customerNameTextBox), 0, 0);
        layout.Controls.Add(_customerNameTextBox, 1, 0);
        layout.Controls.Add(CreateLabel("RoleLabel", "Role:", _roleComboBox), 2, 0);
        layout.Controls.Add(_roleComboBox, 3, 0);

        var actions = new FlowLayoutPanel
        {
            Name = "ActionPanel",
            AutoSize = true,
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(0, 7, 0, 3)
        };
        actions.Controls.AddRange(
        [
            _enabledCheckBox,
            _saveButton,
            _modalDialogButton,
            _disabledActionButton,
            _delayedActionButton
        ]);
        layout.Controls.Add(actions, 0, 1);
        layout.SetColumnSpan(actions, 4);

        var statuses = new TableLayoutPanel
        {
            Name = "StatusPanel",
            AutoSize = true,
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        statuses.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statuses.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statuses.Controls.Add(_validationStatusLabel, 0, 0);
        statuses.Controls.Add(_delayedStatusLabel, 1, 0);
        layout.Controls.Add(statuses, 0, 2);
        layout.SetColumnSpan(statuses, 4);

        group.Controls.Add(layout);
        return group;
    }

    private Control CreateContentPanel()
    {
        var split = new SplitContainer
        {
            Name = "ContentSplitContainer",
            AccessibleName = "Navigation and test content",
            AccessibleDescription = "Separates the navigation tree from the tabbed test content.",
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel1,
            IsSplitterFixed = true,
            SplitterDistance = 285,
            TabStop = false
        };

        var treeGroup = new GroupBox
        {
            Name = "NavigationGroup",
            AccessibleName = "Navigation",
            AccessibleDescription = "Contains the expandable navigation tree.",
            Text = "Navigation",
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        treeGroup.Controls.Add(_navigationTreeView);
        split.Panel1.Controls.Add(treeGroup);

        var gridPage = new TabPage
        {
            Name = "CustomersTabPage",
            AccessibleName = "Customers tab",
            AccessibleDescription = "Contains the fixed customer data grid.",
            Text = "Customers",
            Padding = new Padding(8)
        };
        gridPage.Controls.Add(_customerDataGrid);

        var detailsPage = new TabPage
        {
            Name = "DetailsTabPage",
            AccessibleName = "Details tab",
            AccessibleDescription = "Contains expandable customer details.",
            Text = "Details",
            Padding = new Padding(8)
        };
        detailsPage.Controls.Add(_expandableDetailsGroup);

        _mainTabControl.TabPages.AddRange([gridPage, detailsPage]);
        split.Panel2.Controls.Add(_mainTabControl);
        return split;
    }

    private MenuStrip CreateMenu()
    {
        var menu = new MenuStrip
        {
            Name = "MainMenuStrip",
            AccessibleName = "Application menu",
            AccessibleDescription = "Contains deterministic fixture commands.",
            Dock = DockStyle.Top
        };

        var actionsMenu = new ToolStripMenuItem
        {
            Name = "ActionsMenuItem",
            AccessibleName = "Actions menu",
            AccessibleDescription = "Opens the application actions menu.",
            Text = "&Actions"
        };

        _resetMenuItem.Name = "ResetMenuItem";
        _resetMenuItem.AccessibleName = "Reset fixture";
        _resetMenuItem.AccessibleDescription = "Restores every interactive control to its initial state.";
        _resetMenuItem.Text = "&Reset fixture";
        actionsMenu.DropDownItems.Add(_resetMenuItem);
        menu.Items.Add(actionsMenu);
        MainMenuStrip = menu;
        return menu;
    }

    private static Label CreateLabel(string name, string text, Control target)
    {
        var label = new Label
        {
            Name = name,
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 6, 8, 3)
        };
        label.AccessibleName = text.TrimEnd(':');
        label.AccessibleDescription = $"Label for {target.AccessibleName}.";
        return label;
    }

    private void WireEvents()
    {
        _saveButton.Click += (_, _) => SaveCustomer();
        _enabledCheckBox.CheckedChanged += (_, _) =>
            _validationStatusLabel.Text = _enabledCheckBox.Checked
                ? "Customer enabled."
                : "Customer disabled.";
        _resetMenuItem.Click += (_, _) => ResetFixture();
        _toggleDetailsButton.Click += (_, _) => SetDetailsExpanded(!_expandableContentPanel.Visible);
        _modalDialogButton.Click += (_, _) => ShowModalDialog();
        _delayedActionButton.Click += (_, _) => StartDelayedAction();
        _delayedTimer.Tick += (_, _) => CompleteDelayedAction();
    }

    private void SaveCustomer()
    {
        var customerName = _customerNameTextBox.Text.Trim();
        _validationStatusLabel.Text = customerName.Length == 0
            ? "Customer name is required."
            : $"Saved customer: {customerName}";
    }

    private void ShowModalDialog()
    {
        using var dialog = new FixtureDialog();
        dialog.ShowDialog(this);
        _validationStatusLabel.Text = "Modal dialog closed.";
    }

    private void StartDelayedAction()
    {
        _delayedTimer.Stop();
        _delayedStatusLabel.Text = "Delayed action pending";
        _delayedActionButton.Text = "Waiting...";
        _delayedActionButton.Enabled = false;
        _delayedTimer.Start();
    }

    private void CompleteDelayedAction()
    {
        _delayedTimer.Stop();
        _delayedStatusLabel.Text = "Delayed action complete";
        _delayedActionButton.Text = "Run delayed action";
        _delayedActionButton.Enabled = true;
    }

    private void SetDetailsExpanded(bool expanded)
    {
        _expandableContentPanel.Visible = expanded;
        _toggleDetailsButton.Text = expanded ? "Collapse details" : "Expand details";
        _toggleDetailsButton.AccessibleName = expanded
            ? "Collapse customer details"
            : "Expand customer details";
    }

    private void ResetFixture()
    {
        _delayedTimer.Stop();
        _customerNameTextBox.Clear();
        _enabledCheckBox.Checked = true;
        _roleComboBox.SelectedIndex = 1;
        _validationStatusLabel.Text = ReadyStatus;
        _delayedStatusLabel.Text = DelayedReadyStatus;
        _delayedActionButton.Text = "Run delayed action";
        _delayedActionButton.Enabled = true;
        _mainTabControl.SelectedIndex = 0;
        _navigationTreeView.CollapseAll();
        _navigationTreeView.SelectedNode = null;
        _customerDataGrid.ClearSelection();
        SetDetailsExpanded(true);
    }
}

internal sealed class FixtureDialog : Form
{
    public FixtureDialog()
    {
        Name = "FixtureDialog";
        AccessibleName = "Fixture modal dialog";
        AccessibleDescription = "A deterministic modal dialog for automation tests.";
        Text = "Fixture Dialog";
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = new Size(360, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var message = new Label
        {
            Name = "ModalDialogMessageLabel",
            AccessibleName = "Modal dialog message",
            AccessibleDescription = "The fixed message displayed by the modal dialog.",
            Text = "This is a deterministic modal dialog.",
            AutoSize = true,
            Location = new Point(24, 28)
        };

        var okButton = new Button
        {
            Name = "ModalDialogOkButton",
            AccessibleName = "Close modal dialog",
            AccessibleDescription = "Closes the deterministic modal dialog.",
            Text = "OK",
            DialogResult = DialogResult.OK,
            Size = new Size(90, 30),
            Location = new Point(246, 92)
        };

        AcceptButton = okButton;
        CancelButton = okButton;
        Controls.Add(message);
        Controls.Add(okButton);
    }
}
