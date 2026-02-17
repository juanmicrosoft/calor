'use client';

import { MessageCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { trackAskCalorClick } from '@/lib/analytics';

export function AskCalor() {
  return (
    <section className="py-16">
      <div className="mx-auto max-w-7xl px-6 lg:px-8">
        <div className="mx-auto max-w-3xl text-center">
          <div className="flex justify-center mb-6">
            <div className="flex h-16 w-16 items-center justify-center rounded-full bg-calor-pink">
              <MessageCircle className="h-8 w-8 text-white" />
            </div>
          </div>
          <h2 className="text-3xl font-bold tracking-tight sm:text-4xl">
            Ask Calor
          </h2>
          <p className="mt-4 text-lg text-muted-foreground">
            Have questions about Calor? Chat with our custom GPT to learn about syntax,
            best practices, and how to get the most out of the language.
          </p>
          <div className="mt-8">
            <Button size="lg" asChild>
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
