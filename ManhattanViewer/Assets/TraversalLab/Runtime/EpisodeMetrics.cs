using System;
using System.Globalization;
using System.IO;
using WebShooter.Swing;
namespace Manhattan.TraversalLab
{
    public sealed class EpisodeMetrics
    {
        public float Duration { get; private set; }
        public float AirTime { get; private set; }
        public float SwingTime { get; private set; }
        public float SpeedIntegral { get; private set; }
        public float TransitionLoss { get; private set; }
        public int Transitions { get; private set; }
        public int MaxWrapPoints { get; set; }
        public int Shots { get; set; }
        public int Attachments { get; set; }
        public float MeanSpeed=>Duration>0?SpeedIntegral/Duration:0;
        public float MeanTransitionLoss=>Transitions>0?TransitionLoss/Transitions:0;
        public void Observe(TraversalSample s)
        {
            Duration+=s.dt;SpeedIntegral+=s.velocity.magnitude*s.dt;
            if(s.state!=TraversalState.Grounded)AirTime+=s.dt;
            if(s.state==TraversalState.Swinging || s.state==TraversalState.Yanking)SwingTime+=s.dt;
            if(s.state!=s.previousState){Transitions++;TransitionLoss+=Math.Max(0,s.previousVelocity.magnitude-s.velocity.magnitude);}
        }
        public const string Header="seed,episode,condition,success,duration,snags,stucks,mean_speed,air_fraction,transition_loss,goal_time,swing_fraction,shots,attachments,max_wrap_points,transitions,transition_loss_total";
        public string Row(int seed,int episode,string condition,bool success,int snags,int stucks)
        {
            var c=CultureInfo.InvariantCulture;string F(float x)=>x.ToString("F6",c);
            return string.Join(",",seed,episode,TraversalCsv.Quote(condition),success?1:0,F(Duration),snags,stucks,F(MeanSpeed),
                F(Duration>0?AirTime/Duration:0),F(MeanTransitionLoss),success?F(Duration):"",F(Duration>0?SwingTime/Duration:0),Shots,Attachments,MaxWrapPoints,Transitions,F(TransitionLoss));
        }
    }
}
