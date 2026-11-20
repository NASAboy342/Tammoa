using Newtonsoft.Json.Linq;

namespace Tammoa.Services;

public interface IFlowDefinitionValidator
{
    /// <summary>Checks the shape of a flow definition { nodes: [{id,type,name,position,params}], edges: [{id,source,target}] }.</summary>
    /// <exception cref="ArgumentException">Any structural problem, with a message the UI can show (400).</exception>
    void Validate(JObject definition);
}
