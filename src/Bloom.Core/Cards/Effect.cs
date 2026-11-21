using System.Text.Json.Serialization;

namespace Bloom.Core.Cards;

/// <summary>
/// One thing a card or an adjacency bonus does. In data files, an effect is an object whose first
/// property is <c>"type"</c>: <c>{ "type": "damage", "amount": 5 }</c>. The type must come first,
/// because .NET 8's JSON reader requires the discriminator before the other properties.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DamageEffect), "damage")]
[JsonDerivedType(typeof(BlockEffect), "block")]
[JsonDerivedType(typeof(GainEnergyEffect), "gainEnergy")]
[JsonDerivedType(typeof(DrawEffect), "draw")]
[JsonDerivedType(typeof(RepairEffect), "repair")]
[JsonDerivedType(typeof(FocusEffect), "focus")]
[JsonDerivedType(typeof(GrowEffect), "grow")]
[JsonDerivedType(typeof(DamageBonusEffect), "damageBonus")]
[JsonDerivedType(typeof(StartGrownEffect), "startGrown")]
public abstract record Effect;

/// <summary>Deal damage to the enemy. Enemy block absorbs it first.</summary>
public sealed record DamageEffect(int Amount) : Effect;

/// <summary>Gain block until the end of the enemy's next action.</summary>
public sealed record BlockEffect(int Amount) : Effect;

public sealed record GainEnergyEffect(int Amount) : Effect;

public sealed record DrawEffect(int Count) : Effect;

/// <summary>Bring disabled pieces back during a fight.</summary>
public sealed record RepairEffect(int Count) : Effect;

/// <summary>The next attack deals this much more damage.</summary>
public sealed record FocusEffect(int Amount) : Effect;

/// <summary>Each time the card is played, it gains this much block for the rest of the fight.</summary>
public sealed record GrowEffect(int Amount) : Effect;

/// <summary>
/// An effect that only an adjacency bonus can carry, never a card. Registered again here because
/// polymorphism attributes are not inherited, and <see cref="Pieces.AdjacencyBonus"/> declares this type.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(DamageBonusEffect), "damageBonus")]
[JsonDerivedType(typeof(StartGrownEffect), "startGrown")]
public abstract record BonusEffect : Effect;

/// <summary>Cards of the receiving piece deal this much more damage.</summary>
public sealed record DamageBonusEffect(int Amount) : BonusEffect;

/// <summary>Cards of the receiving piece start each fight already grown this many times.</summary>
public sealed record StartGrownEffect(int Times) : BonusEffect;
