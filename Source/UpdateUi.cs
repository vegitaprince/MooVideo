namespace MooVideo;

public sealed partial class PlayerForm
{
    sealed class UpdatePrompt : Form
    {
        protected override bool ShowWithoutActivation => true;
    }

    CancellationTokenSource? updateToken;
    PreparedUpdate? preparedUpdate;
    bool offerUpdate;
    Form? updatePrompt;
    Button? updateInstallButton;
    Label? updatePromptMessage;

    Task CheckForUpdates(bool manual) => CheckForUpdates(manual, Updates.CheckAsync, Updates.PrepareAsync);

    async Task CheckForUpdates(bool manual,
        Func<CancellationToken, Task<AvailableUpdate?>> check,
        Func<AvailableUpdate, IProgress<double>, CancellationToken, Task<PreparedUpdate>> prepare)
    {
        if (closing || updateToken != null) return;
        if (preparedUpdate != null)
        {
            if (manual)
            {
                if (updatePrompt != null) updatePrompt.Activate();
                else offerUpdate = true;
            }
            return;
        }

        using var operation = new CancellationTokenSource();
        updateToken = operation;
        try
        {
            if (manual || current == null) status.Text = "업데이트를 확인하는 중…";
            var available = await check(operation.Token);
            if (closing) return;
            prefs.LastUpdateCheckUtc = DateTime.UtcNow;
            if (available == null)
            {
                if (manual || current == null) status.Text = "최신 버전입니다.";
                if (manual) MessageBox.Show(this, "최신 버전입니다.", "MooVideo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            status.Text = "업데이트 다운로드 중…";
            SetUpdatingDisplay(true);
            var progress = new Progress<double>(fraction =>
            {
                if (closing || updateToken != operation) return;
                status.Text = fraction >= 1 ? "업데이트 파일을 검증하는 중…" : $"업데이트 다운로드 {(int)(fraction * 100)}%";
            });
            var ready = await prepare(available, progress, operation.Token);
            if (closing)
            {
                Updates.DiscardStage(ready.StageDirectory);
                return;
            }
            preparedUpdate = ready;
            offerUpdate = true;
            status.Text = "업데이트 설치 준비 완료";
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!closing)
            {
                status.Text = "업데이트 확인 또는 다운로드 실패";
                if (manual) Error(new InvalidOperationException("업데이트를 확인하거나 다운로드하지 못했습니다.\n" + ex.Message));
            }
        }
        finally
        {
            updateToken = null;
            if (!closing) SetUpdatingDisplay(false);
        }
    }

    void OfferPreparedUpdate()
    {
        if (updatePrompt != null)
        {
            updateInstallButton!.Enabled = !exportBusy;
            updatePromptMessage!.Text = exportBusy ? "편집 작업이 끝나면 설치할 수 있습니다." : "설치하면 MooVideo가 다시 시작됩니다.";
            return;
        }
        if (!offerUpdate || preparedUpdate == null || closing || exportBusy || WindowState == FormWindowState.Minimized || !Native.IsWindowEnabled(Handle)) return;

        offerUpdate = false;
        var screen = Screen.FromControl(this).Bounds;
        var prompt = new UpdatePrompt
        {
            Text = "MooVideo 업데이트", ShowInTaskbar = false, MinimizeBox = false, MaximizeBox = false,
            FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.Manual, Location = screen.Location,
            AutoScaleMode = AutoScaleMode.Font, Font = Font, BackColor = Theme.Panel, ForeColor = Theme.Text,
            ClientSize = new Size(384, 158)
        };
        var heading = Theme.Label($"MooVideo {preparedUpdate.Version.ToString(3)} 설치 준비 완료", 12);
        heading.SetBounds(20, 16, 344, 34);
        var message = Theme.Label("설치하면 MooVideo가 다시 시작됩니다.", 10, Theme.Muted);
        message.SetBounds(20, 50, 344, 30);
        var install = new Button { Text = "설치 및 재시작", Size = new Size(136, 40), Location = new Point(228, 98), TabIndex = 1 };
        var later = new Button { Text = "나중에", Size = new Size(96, 40), Location = new Point(122, 98), TabIndex = 0 };
        foreach (var button in new[] { later, install })
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Theme.Line;
            button.BackColor = button == install ? Theme.Line : Theme.Field;
        }
        later.Click += (_, _) => prompt.Close();
        install.Click += (_, _) => InstallPreparedUpdate();
        prompt.Controls.AddRange([heading, message, later, install]);
        prompt.CancelButton = later;
        prompt.ActiveControl = later;
        prompt.FormClosed += (_, _) =>
        {
            updatePrompt = null;
            updateInstallButton = null;
            updatePromptMessage = null;
            if (!closing && preparedUpdate != null) status.Text = "업데이트를 나중에 설치합니다.";
        };
        updatePrompt = prompt;
        updateInstallButton = install;
        updatePromptMessage = message;
        prompt.Load += (_, _) => prompt.Location = new Point(
            screen.Left + (screen.Width - prompt.Width) / 2,
            screen.Top + (screen.Height - prompt.Height) / 2);
        prompt.Show(this);
    }

    void InstallPreparedUpdate()
    {
        if (closing || exportBusy || preparedUpdate == null) return;
        try
        {
            Updates.StartInstallation(preparedUpdate, current ?? startupFiles.FirstOrDefault(File.Exists));
        }
        catch (Exception ex)
        {
            MessageBox.Show(updatePrompt, "업데이트 설치를 시작하지 못했습니다.\n" + ex.Message,
                "MooVideo 업데이트", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        preparedUpdate = null; // The installer now owns the staged files.
        Close();
    }

    void CancelUpdate()
    {
        updateToken?.Cancel();
        if (preparedUpdate != null) Updates.DiscardStage(preparedUpdate.StageDirectory);
    }
}
