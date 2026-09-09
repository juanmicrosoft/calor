import type { Metadata } from 'next';
import { DM_Sans, Raleway, JetBrains_Mono, VT323 } from 'next/font/google';
import { GoogleAnalytics } from '@next/third-parties/google';
import { Header } from '@/components/Header';
import { Footer } from '@/components/Footer';
import { WhatsNewBanner } from '@/components/landing/WhatsNewBanner';
import './globals.css';
import { themeScript } from '@/lib/theme';
import { canonicalUrl, publicUrl, siteOrigin } from '@/lib/site';
import { getBasePath } from '@/lib/utils';

const dmSans = DM_Sans({
  subsets: ['latin'],
  variable: '--font-body',
});

const displayFont = Raleway({
  weight: ['400', '500', '600', '700'],
  subsets: ['latin'],
  variable: '--font-display',
});

const jetbrainsMono = JetBrains_Mono({
  subsets: ['latin'],
  variable: '--font-mono',
});

const vt323 = VT323({
  weight: '400',
  subsets: ['latin'],
  variable: '--font-terminal',
});

export const metadata: Metadata = {
  metadataBase: new URL(siteOrigin),
  alternates: { canonical: canonicalUrl('/') },
  title: {
    default: 'Calor - Coding Agent Language for Optimized Reasoning',
    template: '%s | Calor',
  },
  description:
    'A programming language designed specifically for AI coding agents, compiling to .NET via C# emission.',
  keywords: ['Calor', 'programming language', 'AI', 'coding agents', 'compiler', '.NET', 'C#'],
  authors: [{ name: 'Calor Team' }],
  icons: {
    icon: `${getBasePath()}/favicon.ico`,
  },
  openGraph: {
    type: 'website',
    locale: 'en_US',
    url: canonicalUrl('/'),
    siteName: 'Calor',
    title: 'Calor - A Programming Language for AI Coding Agents',
    description:
      'Calor is a programming language designed for AI coding agents, compiling to .NET via C#. Build intelligent, optimized agents with ease.',
    images: [
      {
        url: publicUrl('/og-image.jpg'),
        width: 1200,
        height: 630,
        alt: 'Calor - A Programming Language for AI Coding Agents',
      },
    ],
  },
  twitter: {
    card: 'summary_large_image',
    title: 'Calor - A Programming Language for AI Coding Agents',
    description:
      'Calor is a programming language designed for AI coding agents, compiling to .NET via C#. Build intelligent, optimized agents with ease.',
    images: [publicUrl('/og-image.jpg')],
  },
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en" suppressHydrationWarning>
      <head><script dangerouslySetInnerHTML={{ __html: themeScript }} /></head>
      <body className={`${dmSans.variable} ${displayFont.variable} ${jetbrainsMono.variable} ${vt323.variable} font-body antialiased`}>
        <div className="relative flex min-h-screen flex-col">
          <WhatsNewBanner />
          <Header />
          <main className="flex-1">{children}</main>
          <Footer />
        </div>
      </body>
      <GoogleAnalytics gaId="G-GR98MMDPX3" />
    </html>
  );
}
