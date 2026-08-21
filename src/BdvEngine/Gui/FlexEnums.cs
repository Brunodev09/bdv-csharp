namespace BdvEngine.Gui;

/// <summary>Direction the container flows its children.</summary>
public enum FlexDirection { Row, Column }

/// <summary>Main-axis distribution — how leftover space is spread.</summary>
public enum FlexJustify
{
    Start,          // pack at the start
    Center,         // centered as a group
    End,            // pack at the end
    SpaceBetween,   // first/last touch edges, equal gaps between
    SpaceAround,    // half-gap outside, full-gap inside
    SpaceEvenly,    // equal gap everywhere including edges
}

/// <summary>Cross-axis alignment for each child.</summary>
public enum FlexAlign
{
    Start,     // top of row / left of column
    Center,    // centered across the axis
    End,       // bottom of row / right of column
    Stretch,   // fill the cross axis
}
