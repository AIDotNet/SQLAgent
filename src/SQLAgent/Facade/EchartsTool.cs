using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace SQLAgent.Facade;

public class EchartsTool
{
    public string EchartsOption = string.Empty;

    [KernelFunction("Write"), Description(
         """
         Writes the generated Echarts option.

         Usage:
         - This tool should be called when you have generated the final Echarts option.
         - The option will be directly written and used.
         - Ensure the Echarts option is correct and complete before calling this tool.
         """)]
    public string Write(string option)
    {
        EchartsOption = option;
        return """
               <system-remind>
               The Echarts option has been written and completed.
               </system-remind>
               """;
    }
}