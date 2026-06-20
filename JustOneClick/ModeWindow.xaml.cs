using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Text.Json;

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

        public ModeWindow()
        {
            InitializeComponent();
            LoadCustomPrompt();
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
                default: RbAssistant.IsChecked = true; break;
            }
        }

        private void LoadCustomPrompt()
        {
            try
            {
                if (!File.Exists(SavePath)) return;

                var json = File.ReadAllText(SavePath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("customPrompt", out var el))
                    CustomPromptBox.Text = el.GetString() ?? "";
            }
            catch { /* файл повреждён — игнорируем */ }
        }

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
            }
            // Для своего режима — не трогаем, там уже загруженный текст

        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }

        private void BtnSaveCustom_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SavePath)!);

                var data = new { customPrompt = CustomPromptBox.Text };
                var json = JsonSerializer.Serialize(data,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(SavePath, json);

                // Уведомление через Owner (MainWindow)
                if (Owner is MainWindow mw)
                    mw.ShowNotification("💾 Свой режим сохранён");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Ошибка сохранения: " + ex.Message);
            }
         
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
                ResultModeName = "Свой режим";
                ResultPrompt = CustomPromptBox.Text;
            }

            DialogResult = true; // сигнал что нажали Применить
            Close();
        }
        #endregion
    
    }

}
