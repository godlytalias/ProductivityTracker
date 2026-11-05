using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace ProductivityTracker
{
    public sealed partial class InsightCard : ContentControl
    {
        protected override AutomationPeer OnCreateAutomationPeer() => new InsightCardAutomationPeer(this);
    }

    internal sealed partial class InsightCardAutomationPeer : FrameworkElementAutomationPeer
    {
        public InsightCardAutomationPeer(InsightCard owner) : base(owner)
        {
        }

        protected override string GetClassNameCore() => nameof(InsightCard);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;
    }
}
