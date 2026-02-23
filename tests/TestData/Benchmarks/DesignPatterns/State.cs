using System;

namespace DesignPatterns
{
    public enum TrafficState { Green, Yellow, Red }

    public class TrafficLight
    {
        public TrafficState State { get; private set; } = TrafficState.Green;

        public void Next()
        {
            State = State switch
            {
                TrafficState.Green => TrafficState.Yellow,
                TrafficState.Yellow => TrafficState.Red,
                TrafficState.Red => TrafficState.Green,
                _ => TrafficState.Green
            };
        }

        public bool CanGo => State == TrafficState.Green;
        public bool ShouldStop => State == TrafficState.Red;
    }
}
