using System.Text;

namespace Threefold.DevTools
{
    /// <summary>
    /// Something that can describe its state to the development diagnostics, such as a level's puzzle
    /// progression. It is only asked while the diagnostics are shown.
    /// </summary>
    public interface IDevReport
    {
        string ReportTitle { get; }

        void AppendReport(StringBuilder text);
    }
}
