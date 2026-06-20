using System;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using static JustOneClick.MainWindow;

namespace JustOneClick
{
    /// <summary>
    /// Логика взаимодействия для Window1.xaml
    /// </summary>
    public partial class ModeWindow : Window
    {

        // Путь к файлу с кастомным промптом
        private static readonly string SavePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "JustOneClick",
            "modes.json");

        // Результат — системный промпт который вернём в MainWindow
        public string? ResultPrompt { get; private set; }
        public string? ResultModeName { get; private set; }

        // Текущий активный промпт (передаём из MainWindow)
        public string? CurrentPrompt { get; set; }
        public string? CurrentModeName { get; set; }

        // ═══ Список сохранённых режимов в памяти ═══
        private List<SavedMode> _savedModes = new();

        public ModeWindow()
        {
            InitializeComponent();
            LoadAllModes();
            RefreshModesList();   // заполняем ListBox
            RestoreSelection();
        }

        #region Область общих методов
        private void RestoreSelection()
        {
            switch (CurrentModeName)
            {
                case "Историк": RbHistorian.IsChecked = true; break;
                case "Рассказчик": RbStoryteller.IsChecked = true; break;
                case "Свой режим": RbCustom.IsChecked = true; break;
                // Если имя совпадает с одним из сохранённых — выбираем "Свой режим"
                default:
                    if (_savedModes.Any(m => m.Name == CurrentModeName))
                    {
                        RbCustom.IsChecked = true;
                        var mode = _savedModes.First(m => m.Name == CurrentModeName);
                        CustomNameBox.Text = mode.Name;
                        CustomPromptBox.Text = mode.Prompt;
                    }
                    else
                    {
                        RbAssistant.IsChecked = true;
                    }
                    break;
            }
        }

        private void LoadAllModes()
        {
            try
            {
                if (!File.Exists(SavePath)) return;

                var json = File.ReadAllText(SavePath);

                // Добавляем PropertyNameCaseInsensitive = true
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var data = JsonSerializer.Deserialize<ModesFile>(json, options);
                _savedModes = data?.Modes ?? new List<SavedMode>();
            }
            catch { _savedModes = new List<SavedMode>(); }
        }

        private void SaveAllModes()
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SavePath)!);
                var data = new ModesFile { Modes = _savedModes };
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    // Сохраняем с маленькой буквы — camelCase
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };
                var json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(SavePath, json);
            }
            catch (Exception ex)
            {
                ShowError("Ошибка сохранения: " + ex.Message);
            }
        }

        private void RefreshModesList()
        {
            SavedModesList.Items.Clear();
            foreach (var mode in _savedModes)
                SavedModesList.Items.Add(mode.Name);
        }

        private void ShowError(string msg) => MessageBox.Show(msg, "JustOneClick", MessageBoxButton.OK, MessageBoxImage.Warning);
    
                    


        #endregion

        #region Область кнопок
        private void BtnClose_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        protected override void OnMouseLeftButtonDown( System.Windows.Input.MouseButtonEventArgs e) { base.OnMouseLeftButtonDown(e); DragMove(); }

        private void Mode_Checked(object sender, RoutedEventArgs e)
        {
            if (CustomPromptBox == null) return;

            if (sender is RadioButton rb && rb != RbCustom)
            {
                // Tag содержит промпт фиксированного режима
                CustomPromptBox.Text = rb.Tag?.ToString() ?? "";
                CustomNameBox.Text = "";
            }
            // Для своего режима — не трогаем, там уже загруженный текст

        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private void BtnSaveCustom_Click(object sender, RoutedEventArgs e)
        {
            var name = CustomNameBox.Text.Trim();
            var prompt = CustomPromptBox.Text.Trim();

            if (string.IsNullOrEmpty(name))
            {
                ShowError("Введи название режима");
                return;
            }
            if (string.IsNullOrEmpty(prompt))
            {
                ShowError("Введи текст промпта");
                return;
            }

            // Если имя уже есть — обновляем, иначе добавляем
            var existing = _savedModes.FirstOrDefault(m => m.Name == name);
            if (existing != null)
            {
                existing.Prompt = prompt; // обновляем промпт
            }
            else
            {
                _savedModes.Add(new SavedMode { Name = name, Prompt = prompt });
            }

            SaveAllModes();
            RefreshModesList();

            if (Owner is MainWindow mw)
                mw.ShowNotification($"💾 Режим «{name}» сохранён");

        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (RbAssistant.IsChecked == true)
            {
                ResultModeName = "Ассистент";
                ResultPrompt = RbAssistant.Tag?.ToString();
            }
            else if (RbHistorian.IsChecked == true)
            {
                ResultModeName = "Историк";
                ResultPrompt = RbHistorian.Tag?.ToString();
            }
            else if (RbStoryteller.IsChecked == true)
            {
                ResultModeName = "Рассказчик";
                ResultPrompt = RbStoryteller.Tag?.ToString();
            }
            else if (RbCustom.IsChecked == true)
            {
                var name = CustomNameBox.Text.Trim();
                ResultModeName = string.IsNullOrEmpty(name) ? "Свой режим" : name;
                ResultPrompt = CustomPromptBox.Text;
            }

            DialogResult = true; // сигнал что нажали Применить
            Close();
        }
        

        private void BtnMyModes_Click(object sender, RoutedEventArgs e)
        {
            bool isVisible = SavedModesPanel.Visibility == Visibility.Visible;

            SavedModesPanel.Visibility = isVisible
                ? Visibility.Collapsed
                : Visibility.Visible;

            BtnMyModes.Content = isVisible
                ? "📂  Мои режимы"
                : "📂  Мои режимы ▲";

            // Подгоняем высоту окна
            Height = isVisible ? 580 : 720;
        }

        private void SavedModesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SavedModesList.SelectedItem is not string selectedName) return;

            var mode = _savedModes.FirstOrDefault(m => m.Name == selectedName);
            if (mode == null) return;

            // Переключаемся на "Свой режим" и заполняем поля
            RbCustom.IsChecked = true;
            CustomNameBox.Text = mode.Name;
            CustomPromptBox.Text = mode.Prompt;
        }

        private void BtnDeleteMode_Click(object sender, RoutedEventArgs e)
        {
            if (SavedModesList.SelectedItem is not string selectedName) return;

            _savedModes.RemoveAll(m => m.Name == selectedName);
            SaveAllModes();
            RefreshModesList();

            // Сбрасываем поля если удалили активный
            if (CustomNameBox.Text == selectedName)
            {
                CustomNameBox.Text = "";
                CustomPromptBox.Text = "";
                RbAssistant.IsChecked = true;
            }
        }

        #endregion

        #region Область класссов встроенных в ModeWindow

        public class SavedMode
        {
            public string Name { get; set; } = "";
            public string Prompt { get; set; } = "";
        }

        public class ModesFile
        {
            public List<SavedMode> Modes { get; set; } = new();
        }

        #endregion

    }

}
