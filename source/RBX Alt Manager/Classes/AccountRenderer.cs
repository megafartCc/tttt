using System;
using System.Drawing;

namespace RBX_Alt_Manager.Classes
{
    public class AccountRenderer : BrightIdeasSoftware.BaseRenderer
    {
        public override void Render(Graphics g, Rectangle r)
        {
            base.Render(g, r);

            Account account = RowObject as Account;
            bool showAging = !AccountManager.General.Get<bool>("DisableAgingAlert");
            TimeSpan diff = DateTime.Now - account.LastUse;
            bool isOld = diff.TotalDays > 20;
            bool renderOldDot = showAging && isOld;

            if (renderOldDot)
            {
                diff -= TimeSpan.FromDays(20);

                using (Brush b = new SolidBrush(Color.FromArgb(255, 255, 204, 77).Lerp(Color.FromArgb(255, 250, 26, 13), (float)Utilities.MapValue(diff.TotalSeconds, 0, 864000, 0, 1).Clamp(0, 1))))
                    g.FillEllipse(b, new Rectangle((int)(r.X + 3f * Program.Scale), (int)(r.Y + 2 * Program.Scale), (int)(4f * Program.Scale), (int)(4f * Program.Scale)));
            }

            Color? indicatorColor = null;

            if (account.HasOpenInstance)
                indicatorColor = account.IsOnServer ? Presence.Colors[UserPresenceType.InGame] : Presence.Colors[UserPresenceType.Online];
            else if (AccountManager.General.Get<bool>("ShowPresence") && account.Presence != null && account.Presence.userPresenceType != UserPresenceType.Offline && Presence.Colors.ContainsKey(account.Presence.userPresenceType))
                indicatorColor = Presence.Colors[account.Presence.userPresenceType];

            if (indicatorColor.HasValue)
                using (Brush b = new SolidBrush(indicatorColor.Value))
                    g.FillEllipse(b, new Rectangle((int)(r.X + 3f * Program.Scale + (renderOldDot ? (int)(6f * Program.Scale) : 0)), (int)(r.Y + 2 * Program.Scale), (int)(4f * Program.Scale), (int)(4f * Program.Scale)));
        }
    }
}
