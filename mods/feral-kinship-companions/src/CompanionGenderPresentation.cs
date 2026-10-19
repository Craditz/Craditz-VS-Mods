#nullable enable
using Vintagestory.API.Common.Entities;
namespace FeralKinshipCompanions;
internal static class CompanionGenderPresentation
{
    internal static string FromEntity(Entity? entity)=>entity!=null&&CompanionBreedingCatalog.TryGetGender(entity,out string gender)?gender:string.Empty;
}
