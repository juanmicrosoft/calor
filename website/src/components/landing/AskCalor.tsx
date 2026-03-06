'use client';

import { MessageCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { trackAskCalorClick } from '@/lib/analytics';
import { useScrollReveal } from '@/hooks/useScrollReveal';

export function AskCalor() {
  const sectionRef = useScrollReveal<HTMLDivElement>();

  return (
    <section className="relative py-20 overflow-hidden">
      {/* Warm gradient background band */}
      <div className="absolute inset-0 -z-10 bg-gradient-to-br from-calor-pink/5 via-calor-salmon/8 to-calor-cyan/5" />
      <div className="gradient-mesh gradient-mesh-salmon absolute top-0 left-1/3 w-[600px] h-[400px] -z-10" />

      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-3xl text-center" ref={sectionRef}>
          <div className="flex justify-center mb-6">
            <div className="flex h-16 w-16 items-center justify-center rounded-full bg-gradient-to-br from-calor-pink to-calor-salmon shadow-lg shadow-calor-pink/25">
              <MessageCircle className="h-8 w-8 text-white" />
            </div>
          </div>
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Ask Calor
          </h2>
          <p className="mt-4 text-lg text-muted-foreground font-body">
            Have questions about Calor? Chat with our custom GPT to learn about syntax,
            best practices, and how to get the most out of the language.
          </p>
          <div className="mt-8">
            <Button size="lg" asChild className="bg-gradient-to-r from-calor-pink to-calor-salmon hover:from-calor-pink/90 hover:to-calor-salmon/90 text-white border-0 shadow-lg shadow-calor-pink/25 font-body">
              <a
                href="https://chatgpt.com/g/g-6994cc69517c8191a0dc7be0bfc00186-ask-calor"
                target="_blank"
                rel="noopener noreferrer"
                onClick={() => trackAskCalorClick('homepage')}
              >
                <MessageCircle className="mr-2 h-5 w-5" />
                Start a Conversation
              </a>
            </Button>
          </div>
        </div>
      </div>
    </section>
  );
}
